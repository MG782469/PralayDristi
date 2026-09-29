using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Services;

/// <summary>
/// The headline feature: GEE finds a new lake -> volume from the DEM -> natural-dam run queued -> when it finishes
/// SimulationRunService raises the CAP alert. Nobody presses a button between the satellite pixel and the map.
/// </summary>
public sealed class LakeToAlertService
{
    private readonly ILakeDetectionRepository _lakes;
    private readonly IUnitOfWork _uow;
    private readonly IFloodSimulationEngine _engine;
    private readonly RunFactory _runs;
    private readonly IRunNotifier _notifier;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<LakeToAlertService> _log;

    public LakeToAlertService(ILakeDetectionRepository lakes, IUnitOfWork uow, IFloodSimulationEngine engine, RunFactory runs,
        IRunNotifier notifier, IDateTimeProvider clock, ILogger<LakeToAlertService> log)
    {
        _lakes = lakes;
        _uow = uow;
        _engine = engine;
        _runs = runs;
        _notifier = notifier;
        _clock = clock;
        _log = log;
    }

    public async Task<IReadOnlyList<Guid>> IngestAsync(IEnumerable<LakeDetectionInput> detections, bool autoSimulate, CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var d in detections)
        {
            var lake = new LakeDetection
            {
                Id = Guid.NewGuid(),
                DetectedUtc = d.Date?.ToUniversalTime() ?? _clock.UtcNow,
                Lat = d.Lat,
                Lon = d.Lon,
                AreaKm2 = d.AreaKm2,
                PreviousAreaKm2 = d.PreviousAreaKm2,
                Polygon = d.Polygon is { Count: > 2 } ? d.Polygon : null,
                Source = Trim(d.Source ?? "GEE Sentinel-2 NDWI", 60),
                Region = Trim(d.Region ?? "", 80)
            };
            await _lakes.AddAsync(lake, ct);
            await _uow.SaveChangesAsync(ct);
            ids.Add(lake.Id);
            await _notifier.LakeAsync(lake.ToDto());

            if (!autoSimulate) continue;
            try
            {
                await EstimateAndQueueAsync(lake, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // one bad detection must not stop the rest of the batch
                _log.LogError(ex, "Lake chain failed for {Id}", lake.Id);
            }
        }
        return ids;
    }

    public async Task<Guid> EstimateAndQueueAsync(Guid lakeId, CancellationToken ct)
    {
        var lake = await _lakes.GetByIdAsync(lakeId, ct) ?? throw new NotFoundException("Lake detection", lakeId);
        return await EstimateAndQueueAsync(lake, ct);
    }

    private async Task<Guid> EstimateAndQueueAsync(LakeDetection lake, CancellationToken ct)
    {
        lake.State = LakeState.Estimating;
        await _uow.SaveChangesAsync(ct);
        try
        {
            var (spec, level, volume, notes) = await _engine.ScenarioForLakeAsync(lake, ct);
            lake.EstimatedLevel = level;
            lake.EstimatedVolumeMcm = volume;
            lake.Notes = notes;
            var run = await _runs.QueueAsync(spec, RunOrigin.LakeWatch, null, lake.Id, ct);
            lake.RunId = run.Id;
            lake.State = LakeState.Simulating;
            await _uow.SaveChangesAsync(ct);
            await _notifier.LakeAsync(lake.ToDto());
            return run.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lake.State = LakeState.Failed;
            lake.Notes = ex.Message;
            await _uow.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Parses the JSON written by gee/lake_watch.py (also accepts a bare array).</summary>
    public static List<LakeDetectionInput> ParseOutbox(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException ex) { throw new BusinessRuleException($"Could not parse detections: {ex.Message}"); }
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

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;
}
