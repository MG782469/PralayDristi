using System.Text.Json;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Jobs;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Services;

/// <summary>Creates a queued run and hands it to the job scheduler. Every entry point (user, dam, lake, atlas) goes through here.</summary>
public sealed class RunFactory
{
    public const string SimulationsQueue = "simulations";

    private readonly ISimulationRunRepository _runs;
    private readonly IUnitOfWork _uow;
    private readonly IBackgroundJobScheduler _scheduler;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUserService _me;

    public RunFactory(ISimulationRunRepository runs, IUnitOfWork uow, IBackgroundJobScheduler scheduler, IDateTimeProvider clock,
        ICurrentUserService me)
    {
        _runs = runs;
        _uow = uow;
        _scheduler = scheduler;
        _clock = clock;
        _me = me;
    }

    public async Task<SimulationRun> QueueAsync(ScenarioSpec spec, RunOrigin origin, int? damId, Guid? lakeId, CancellationToken ct)
    {
        var run = SimulationRun.Queue(spec.Name, spec.Mode.ToString(), JsonSerializer.Serialize(spec, Mappings.Json), origin,
            _clock.UtcNow, damId, lakeId, _me.UserId);
        await _runs.AddAsync(run, ct);
        // persist first: a fast worker must be able to find the run when it picks the job up
        await _uow.SaveChangesAsync(ct);
        string jobId = _scheduler.Enqueue<RunSimulationJob>(j => j.Execute(run.Id), SimulationsQueue);
        run.AttachJob(jobId);
        await _uow.SaveChangesAsync(ct);
        return run;
    }

    public static ScenarioSpec ReadSpec(SimulationRun run) =>
        JsonSerializer.Deserialize<ScenarioSpec>(run.SpecJson, Mappings.Json) ?? new ScenarioSpec();
}
