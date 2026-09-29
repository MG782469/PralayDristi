using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PralayDrishti.Api.Data;
using PralayDrishti.Api.Hubs;
using PralayDrishti.Core.Geo;
using PralayDrishti.Core.External;
using PralayDrishti.Core.Pipeline;

namespace PralayDrishti.Api.Services;

public sealed class SimulationWorker : BackgroundService
{
    private readonly RunQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly IHubContext<RunHub> _hub;
    private readonly DemProvider _dem;
    private readonly ExposureProvider _exposure;
    private readonly RunStore _store;
    private readonly AppOptions _opt;
    private readonly DataPaths _paths;
    private readonly ILogger<SimulationWorker> _log;
    private readonly SemaphoreSlim _slots;

    public SimulationWorker(RunQueue queue, IServiceScopeFactory scopes, IHubContext<RunHub> hub, DemProvider dem,
        ExposureProvider exposure, RunStore store, AppOptions opt, DataPaths paths, ILogger<SimulationWorker> log)
    {
        _queue = queue;
        _scopes = scopes;
        _hub = hub;
        _dem = dem;
        _exposure = exposure;
        _store = store;
        _opt = opt;
        _paths = paths;
        _log = log;
        _slots = new SemaphoreSlim(Math.Max(1, opt.MaxConcurrentRuns));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in _queue.ReadAllAsync(stoppingToken))
        {
            await _slots.WaitAsync(stoppingToken);
            _ = Task.Run(async () =>
            {
                try { await ProcessAsync(id, stoppingToken); }
                finally { _slots.Release(); }
            }, stoppingToken);
        }
    }

    public async Task ProcessAsync(Guid id, CancellationToken stopping)
    {
        using var cts = _queue.Track(id, stopping);
        var ct = cts.Token;
        try
        {
            ScenarioSpec spec;
            string origin;
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var run = await db.Runs.FindAsync(new object[] { id }, ct);
                if (run == null || run.Status == RunStatus.Cancelled) return;
                run.Status = RunStatus.Running;
                run.StartedUtc = DateTime.UtcNow;
                run.Stage = "Fetching terrain";
                await db.SaveChangesAsync(ct);
                spec = JsonSerializer.Deserialize<ScenarioSpec>(run.SpecJson, RunArchive.Json)!;
                origin = run.Origin;
            }
            await Status(id, "Running");
            await Progress(id, "Fetching terrain", 0.01);

            var center = spec.Mode == ScenarioMode.NaturalDam && spec.NaturalDam?.DebrisPolygon.Count > 0
                ? new GeoPoint(spec.NaturalDam.DebrisPolygon.Average(p => p[0]), spec.NaturalDam.DebrisPolygon.Average(p => p[1]))
                : spec.Dam.Location;
            var dem = await _dem.ForAreaAsync(center, spec.DownstreamKm, spec.UpstreamKm, spec.DemZoom, ct);

            await Progress(id, "Loading exposure (settlements, bridges, roads)", 0.04);
            var (exposure, sources) = await _exposure.ForAreaAsync(dem.Dem.Bounds, ct);

            IFarFieldRunner runner = new InternalSweRunner();
            var notes = new List<string> { $"DEM: {dem.Description}" };
            notes.AddRange(sources.Select(s => "Exposure: " + s));
            if (spec.Engine == FarFieldEngine.Delft3D)
            {
                var d3d = new Delft3DFmAdapter(Path.Combine(_paths.Models, id.ToString("N")), _opt.Delft3DCommand);
                if (d3d.IsConfigured) runner = d3d;
                else notes.Add("Delft3D FM requested but not installed/configured - ran the internal SWE-2D solver; the D-Flow FM model package is available under Exports.");
            }

            double lastPush = 0;
            DateTime lastPushAt = DateTime.MinValue;
            var progress = new Progress<StageProgress>(sp =>
            {
                double overall = MapStage(sp);
                if (overall - lastPush < 0.01 && (DateTime.UtcNow - lastPushAt).TotalSeconds < 2) return;
                lastPush = overall;
                lastPushAt = DateTime.UtcNow;
                _ = Progress(id, sp.Stage, overall);
            });

            var outcome = await Task.Run(() => new ScenarioEngine(runner).Run(spec, dem.Dem, exposure, progress, ct), ct);
            outcome.Prepared.Log.InsertRange(0, notes);

            await Progress(id, "Saving results", 0.98);
            _store.Put(id, outcome);

            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var run = await db.Runs.FindAsync(new object[] { id }, CancellationToken.None);
                if (run != null)
                {
                    run.Status = RunStatus.Completed;
                    run.Progress = 1;
                    run.Stage = "Done";
                    run.FinishedUtc = DateTime.UtcNow;
                    run.SummaryJson = JsonSerializer.Serialize(RunViews.Numbers(outcome, dem.Description, dem.Synthetic), RunArchive.Json);
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                if (origin == "lakewatch")
                    await scope.ServiceProvider.GetRequiredService<AlertService>().CreateFromRunAsync(id, outcome, CancellationToken.None);
            }
            await Status(id, "Completed");
            _log.LogInformation("Run {Id} completed", id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await Finish(id, RunStatus.Cancelled, "Cancelled");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Run {Id} failed", id);
            await Finish(id, RunStatus.Failed, ex.Message);
        }
        finally
        {
            _queue.Untrack(id);
        }
    }

    // stage weights so the progress bar moves sensibly across the whole chain
    private static double MapStage(StageProgress sp)
    {
        string s = sp.Stage;
        double f = Math.Clamp(sp.Fraction, 0, 1);
        if (s.StartsWith("Preparing")) return 0.06 + 0.04 * f;
        if (s.StartsWith("SPH")) return 0.10 + 0.20 * f;
        if (s.StartsWith("2D")) return 0.30 + 0.45 * f;
        if (s.StartsWith("Impact")) return 0.76;
        if (s.StartsWith("Ensemble")) return 0.78 + 0.18 * f;
        return s == "Done" ? 0.97 : 0.5;
    }

    private async Task Finish(Guid id, RunStatus status, string message)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.Runs.FindAsync(id);
        if (run != null)
        {
            run.Status = status;
            run.Error = status == RunStatus.Failed ? message : null;
            run.Stage = message.Length > 110 ? message[..110] : message;
            run.FinishedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            if (run.LakeDetectionId is Guid lid && status == RunStatus.Failed)
            {
                var lake = await db.Lakes.FindAsync(lid);
                if (lake != null) { lake.State = "Failed"; lake.Notes = message; await db.SaveChangesAsync(); }
            }
        }
        await Status(id, status.ToString());
    }

    private async Task Progress(Guid id, string stage, double fraction)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Runs.Where(r => r.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Progress, fraction)
                .SetProperty(r => r.Stage, stage.Length > 110 ? stage.Substring(0, 110) : stage));
            await _hub.Clients.All.SendAsync("progress", id, stage, fraction);
        }
        catch (Exception ex)
        {
            _log.LogDebug("progress push failed: {Msg}", ex.Message);
        }
    }

    private Task Status(Guid id, string status) => _hub.Clients.All.SendAsync("status", id, status);
}
