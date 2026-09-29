using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Options;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Jobs;

/// <summary>Queued by every "run" entry point; executes on the "simulations" queue.</summary>
public sealed class RunSimulationJob
{
    private readonly SimulationRunService _service;

    public RunSimulationJob(SimulationRunService service) => _service = service;

    public Task Execute(Guid runId) => _service.ExecuteAsync(runId, CancellationToken.None);
}

/// <summary>Every minute: picks up files the GEE script dropped in data/gee_outbox (or copied over an air gap).</summary>
public sealed class LakeWatchJob
{
    private readonly IFileStorage _files;
    private readonly LakeToAlertService _chain;
    private readonly LakeWatchOptions _opt;
    private readonly ILogger<LakeWatchJob> _log;

    public LakeWatchJob(IFileStorage files, LakeToAlertService chain, IOptions<LakeWatchOptions> opt, ILogger<LakeWatchJob> log)
    {
        _files = files;
        _chain = chain;
        _opt = opt.Value;
        _log = log;
    }

    public async Task Execute()
    {
        if (!_opt.Enabled) return;
        foreach (var (name, json) in _files.TakeOutbox())
        {
            try
            {
                var detections = LakeToAlertService.ParseOutbox(json);
                _log.LogInformation("Lake watch: {Count} detection(s) in {File}", detections.Count, name);
                await _chain.IngestAsync(detections, _opt.AutoSimulate, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not process {File}: {Msg}", name, ex.Message);
            }
        }
    }
}

/// <summary>Daily: result folders whose run row was deleted are just disk space.</summary>
public sealed class ResultsRetentionJob
{
    private readonly IResultStore _store;
    private readonly ISimulationRunRepository _runs;

    public ResultsRetentionJob(IResultStore store, ISimulationRunRepository runs)
    {
        _store = store;
        _runs = runs;
    }

    public async Task Execute()
    {
        foreach (var id in _store.ListStoredRunIds())
            if (await _runs.GetByIdAsync(id) is null)
                _store.Delete(id);
    }
}
