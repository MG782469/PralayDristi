using System.Text.Json;
using Microsoft.Extensions.Logging;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Services;

/// <summary>
/// Executes one queued run: status bookkeeping, throttled progress, cancellation, results, and the automatic
/// CAP alert for runs raised by the lake watch.
/// </summary>
public sealed class SimulationRunService
{
    private readonly ISimulationRunRepository _runs;
    private readonly ILakeDetectionRepository _lakes;
    private readonly IUnitOfWork _uow;
    private readonly IFloodSimulationEngine _engine;
    private readonly IRunNotifier _notifier;
    private readonly ICacheService _cache;
    private readonly IDateTimeProvider _clock;
    private readonly AlertService _alerts;
    private readonly ILogger<SimulationRunService> _log;

    // one DbContext, progress callbacks arrive on other threads - serialise all saves
    private readonly SemaphoreSlim _dbGate = new(1, 1);

    public SimulationRunService(ISimulationRunRepository runs, ILakeDetectionRepository lakes, IUnitOfWork uow,
        IFloodSimulationEngine engine, IRunNotifier notifier, ICacheService cache, IDateTimeProvider clock, AlertService alerts,
        ILogger<SimulationRunService> log)
    {
        _runs = runs;
        _lakes = lakes;
        _uow = uow;
        _engine = engine;
        _notifier = notifier;
        _cache = cache;
        _clock = clock;
        _alerts = alerts;
        _log = log;
    }

    public async Task ExecuteAsync(Guid runId, CancellationToken stopping)
    {
        var run = await _runs.GetByIdAsync(runId, stopping);
        if (run is null || run.IsFinished) return;   // deleted or cancelled while queued

        run.Start(_clock.UtcNow);
        await _uow.SaveChangesAsync(stopping);
        await _notifier.StatusAsync(runId, RunStatus.Running.ToString());

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var watcher = WatchForCancelAsync(runId, cts);

        double lastPushed = -1;
        DateTime lastPushAt = DateTime.MinValue;
        var progress = new Progress<(string Stage, double Fraction)>(p =>
        {
            if (p.Fraction - lastPushed < 0.01 && (DateTime.UtcNow - lastPushAt).TotalSeconds < 2) return;
            lastPushed = p.Fraction;
            lastPushAt = DateTime.UtcNow;
            _ = PushProgressAsync(runId, p.Stage, p.Fraction);
        });

        try
        {
            var spec = RunFactory.ReadSpec(run);
            var result = await _engine.RunAsync(runId, spec, progress, cts.Token);
            await _dbGate.WaitAsync(CancellationToken.None);
            try
            {
                run.Complete(JsonSerializer.Serialize(result.Numbers, Mappings.Json), _clock.UtcNow);
                await _uow.SaveChangesAsync(CancellationToken.None);
            }
            finally { _dbGate.Release(); }
            await _cache.RemoveAsync(CacheKeys.Leaderboard);
            await _notifier.StatusAsync(runId, RunStatus.Completed.ToString());

            if (run.Origin == RunOrigin.LakeWatch)
                await _alerts.CreateForRunAsync(runId, AlertStatus.Exercise, CancellationToken.None);
            _log.LogInformation("Run {Id} completed", runId);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !stopping.IsCancellationRequested)
        {
            await FinishAsync(run, () => run.Cancel(_clock.UtcNow), RunStatus.Cancelled);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Run {Id} failed", runId);
            await FinishAsync(run, () => run.Fail(ex.Message, _clock.UtcNow), RunStatus.Failed);
            if (run.LakeDetectionId is Guid lid && await _lakes.GetByIdAsync(lid) is { } lake)
            {
                lake.State = LakeState.Failed;
                lake.Notes = ex.Message;
                await _uow.SaveChangesAsync(CancellationToken.None);
            }
        }
        finally
        {
            cts.Cancel();
            try { await watcher; } catch (OperationCanceledException) { }
            await _cache.RemoveAsync(CacheKeys.RunCancel(runId));
        }
    }

    private async Task FinishAsync(Domain.Entities.SimulationRun run, Action change, RunStatus status)
    {
        await _dbGate.WaitAsync(CancellationToken.None);
        try
        {
            change();
            await _uow.SaveChangesAsync(CancellationToken.None);
        }
        finally { _dbGate.Release(); }
        await _notifier.StatusAsync(run.Id, status.ToString());
    }

    private async Task PushProgressAsync(Guid runId, string stage, double fraction)
    {
        if (!await _dbGate.WaitAsync(0)) return;   // a save is in flight - skip this tick, the next one comes soon
        try
        {
            var run = await _runs.GetByIdAsync(runId);
            if (run is null || run.IsFinished) return;
            run.ReportProgress(stage, fraction);
            await _uow.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogDebug("progress save skipped: {Msg}", ex.Message);
        }
        finally { _dbGate.Release(); }
        await _notifier.ProgressAsync(runId, stage, fraction);
    }

    /// <summary>CancelRun sets a cache flag (works across processes / Hangfire servers); we poll it.</summary>
    private async Task WatchForCancelAsync(Guid runId, CancellationTokenSource cts)
    {
        while (!cts.IsCancellationRequested)
        {
            await Task.Delay(2000, cts.Token);
            if (await _cache.GetAsync<bool>(CacheKeys.RunCancel(runId), cts.Token))
            {
                cts.Cancel();
                return;
            }
        }
    }
}
