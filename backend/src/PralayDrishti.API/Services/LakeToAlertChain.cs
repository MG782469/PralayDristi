using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using PralayDrishti.API.Data;
using PralayDrishti.API.Hubs;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Hydraulics;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.API.Services;

public sealed record LakeDetectionInput(
    double Lat, double Lon, double AreaKm2, double? PreviousAreaKm2 = null, List<double[]>? Polygon = null,
    string? Source = null, string? Region = null, DateTime? Date = null);

/// <summary>
/// The headline feature: "Lake-to-Alert autonomous chain".
///   GEE weekly scan (gee/lake_watch.py) finds a new lake -> drops JSON in data/gee_outbox or POSTs it here
///   -> DEM volume estimate -> blockage located at the lake outlet -> natural-dam simulation queued
///   -> when the run finishes the worker raises a CAP alert (AlertService).
/// Nobody has to press a button between the satellite pixel and the evacuation map.
/// </summary>
public sealed class LakeToAlertChain
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DemProvider _dem;
    private readonly RunQueue _queue;
    private readonly IHubContext<RunHub> _hub;
    private readonly AppOptions _opt;
    private readonly ILogger<LakeToAlertChain> _log;

    public LakeToAlertChain(IServiceScopeFactory scopes, DemProvider dem, RunQueue queue, IHubContext<RunHub> hub,
        AppOptions opt, ILogger<LakeToAlertChain> log)
    {
        _scopes = scopes;
        _dem = dem;
        _queue = queue;
        _hub = hub;
        _opt = opt;
        _log = log;
    }

    public async Task<List<Guid>> IngestAsync(IEnumerable<LakeDetectionInput> detections, bool? autoSimulate, CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var d in detections)
        {
            var entity = new LakeDetectionEntity
            {
                Id = Guid.NewGuid(),
                DetectedUtc = d.Date ?? DateTime.UtcNow,
                Lat = d.Lat,
                Lon = d.Lon,
                AreaKm2 = d.AreaKm2,
                PreviousAreaKm2 = d.PreviousAreaKm2,
                PolygonJson = d.Polygon is { Count: > 2 } ? JsonSerializer.Serialize(d.Polygon) : null,
                Source = d.Source ?? "GEE Sentinel-2 NDWI",
                Region = d.Region ?? ""
            };
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Lakes.Add(entity);
                await db.SaveChangesAsync(ct);
            }
            ids.Add(entity.Id);
            await _hub.Clients.All.SendAsync("lake", new { entity.Id, entity.Lat, entity.Lon, entity.AreaKm2, entity.State }, ct);

            if (autoSimulate ?? _opt.LakeWatch.AutoSimulate)
            {
                try { await EstimateAndSimulateAsync(entity.Id, ct); }
                catch (Exception ex) { _log.LogError(ex, "Lake chain failed for {Id}", entity.Id); }
            }
        }
        return ids;
    }

    public async Task<Guid?> EstimateAndSimulateAsync(Guid lakeId, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lake = await db.Lakes.FindAsync(new object[] { lakeId }, ct) ?? throw new KeyNotFoundException("lake");
        lake.State = "Estimating";
        await db.SaveChangesAsync(ct);

        try
        {
            var point = new GeoPoint(lake.Lon, lake.Lat);
            var src = await _dem.ForAreaAsync(point, 30, 5, _opt.DefaultDemZoom, ct);
            var dem = src.Dem;
            var est = SatelliteReservoirEstimator.Estimate(dem, new SatelliteReservoirEstimator.Request(point, lake.AreaKm2, MaxRise: 150));

            // outlet = lake edge cell draining out of the lake with the largest upstream area
            var routing = Hydrology.Route(dem);
            var inLake = new HashSet<int>(est.LakeCells);
            int outlet = -1;
            foreach (int i in est.LakeCells)
            {
                int j = routing.Receivers[i];
                if (j < 0 || inLake.Contains(j)) continue;
                if (outlet < 0 || routing.Accumulation[i] > routing.Accumulation[outlet]) outlet = i;
            }
            if (outlet < 0) throw new InvalidOperationException("could not locate the lake outlet on the DEM");
            int below = routing.Receivers[outlet];
            var c = dem.CellCenter(below);
            double half = Math.Max(dem.Dx, 60) * 2.5;
            var ring = new List<double[]>
            {
                new[] { c.Offset(-half, -half).Lon, c.Offset(-half, -half).Lat },
                new[] { c.Offset(half, -half).Lon, c.Offset(half, -half).Lat },
                new[] { c.Offset(half, half).Lon, c.Offset(half, half).Lat },
                new[] { c.Offset(-half, half).Lon, c.Offset(-half, half).Lat }
            };

            lake.EstimatedLevel = Math.Round(est.EstimatedLevel, 1);
            lake.EstimatedVolumeMcm = Math.Round(est.EstimatedVolumeM3 / 1e6, 3);
            lake.Notes = string.Join(" ", est.Notes) + $" DEM: {src.Description}.";

            var spec = new ScenarioSpec
            {
                Name = $"Auto: new lake {lake.Lat.ToString("F3", CultureInfo.InvariantCulture)}N {lake.Lon.ToString("F3", CultureInfo.InvariantCulture)}E ({lake.AreaKm2:F2} km2)",
                Mode = ScenarioMode.NaturalDam,
                NaturalDam = new NaturalDamSpec
                {
                    DebrisPolygon = ring,
                    CrestElevation = est.EstimatedLevel + 0.5,
                    CurrentLakeLevel = est.EstimatedLevel,
                    Type = DamType.LandslideDam
                },
                DownstreamKm = 30,
                UpstreamKm = 5,
                DurationHours = 3,
                RunSph = false,
                EnsembleMembers = 8,
                DemZoom = _opt.DefaultDemZoom
            };
            var run = ScenarioFactory.NewRun(spec, "lakewatch", null, lake.Id);
            db.Runs.Add(run);
            lake.RunId = run.Id;
            lake.State = "Simulating";
            await db.SaveChangesAsync(ct);
            await _queue.EnqueueAsync(run.Id);
            await _hub.Clients.All.SendAsync("lake", new { lake.Id, lake.Lat, lake.Lon, lake.AreaKm2, lake.State, lake.RunId }, ct);
            return run.Id;
        }
        catch (Exception ex)
        {
            lake.State = "Failed";
            lake.Notes = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Parses the JSON written by gee/lake_watch.py (also accepts a bare array).</summary>
    public static List<LakeDetectionInput> ParseOutbox(string json)
    {
        var node = JsonNode.Parse(json);
        var arr = node is JsonArray a ? a : node?["detections"]?.AsArray() ?? new JsonArray();
        var list = new List<LakeDetectionInput>();
        foreach (var d in arr)
        {
            if (d is null) continue;
            List<double[]>? poly = d["polygon"]?.AsArray().Select(p => new[] { p![0]!.GetValue<double>(), p[1]!.GetValue<double>() }).ToList();
            DateTime? date = DateTime.TryParse(d["date"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt) ? dt : null;
            list.Add(new LakeDetectionInput(
                d["lat"]!.GetValue<double>(), d["lon"]!.GetValue<double>(), d["area_km2"]!.GetValue<double>(),
                d["previous_area_km2"]?.GetValue<double>(), poly, d["source"]?.ToString(), d["region"]?.ToString(), date));
        }
        return list;
    }
}

/// <summary>Watches data/gee_outbox for files dropped by the GEE script (or copied over an air gap on a USB stick).</summary>
public sealed class LakeWatchService : BackgroundService
{
    private readonly LakeToAlertChain _chain;
    private readonly DataPaths _paths;
    private readonly AppOptions _opt;
    private readonly ILogger<LakeWatchService> _log;

    public LakeWatchService(LakeToAlertChain chain, DataPaths paths, AppOptions opt, ILogger<LakeWatchService> log)
    {
        _chain = chain;
        _paths = paths;
        _opt = opt;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.LakeWatch.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var file in Directory.GetFiles(_paths.GeeOutbox, "*.json"))
            {
                try
                {
                    var detections = LakeToAlertChain.ParseOutbox(await File.ReadAllTextAsync(file, stoppingToken));
                    _log.LogInformation("Lake watch: {Count} detection(s) in {File}", detections.Count, Path.GetFileName(file));
                    File.Move(file, Path.Combine(_paths.GeeProcessed, $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Path.GetFileName(file)}"), true);
                    await _chain.IngestAsync(detections, null, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _log.LogWarning("Could not process {File}: {Msg}", file, ex.Message);
                    try { File.Move(file, file + ".bad", true); } catch { /* ignore */ }
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _opt.LakeWatch.PollSeconds)), stoppingToken);
        }
    }
}
