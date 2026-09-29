using System.Linq.Expressions;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.UnitTests.Fakes;

public sealed class FakeDamRepo : IDamRepository
{
    public readonly List<Dam> Items = new();
    public Task<Dam?> GetByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(d => d.Id == id));
    public Task AddAsync(Dam entity, CancellationToken ct = default)
    {
        if (entity.Id == 0) entity.Id = Items.Count + 1;
        Items.Add(entity);
        return Task.CompletedTask;
    }
    public void Remove(Dam entity) => Items.Remove(entity);
    public Task<IReadOnlyList<Dam>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Dam>>(Items.ToList());
}

public sealed class FakeRunRepo : ISimulationRunRepository
{
    public readonly List<SimulationRun> Items = new();
    public Task<SimulationRun?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(r => r.Id == id));
    public Task AddAsync(SimulationRun entity, CancellationToken ct = default) { Items.Add(entity); return Task.CompletedTask; }
    public void Remove(SimulationRun entity) => Items.Remove(entity);
    public Task<IReadOnlyList<SimulationRun>> ListAsync(RunOrigin? origin, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SimulationRun>>(Items.Where(r => origin == null || r.Origin == origin).Take(take).ToList());
    public Task<IReadOnlyList<SimulationRun>> ListByStatusAsync(IEnumerable<RunStatus> statuses, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SimulationRun>>(Items.Where(r => statuses.Contains(r.Status)).ToList());
    public Task<IReadOnlyList<SimulationRun>> LatestCompletedPerDamAsync(RunOrigin origin, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SimulationRun>>(Items.Where(r => r.Origin == origin && r.Status == RunStatus.Completed).ToList());
    public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(Items.Count);
}

public sealed class FakeLakeRepo : ILakeDetectionRepository
{
    public readonly List<LakeDetection> Items = new();
    public Task<LakeDetection?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(l => l.Id == id));
    public Task AddAsync(LakeDetection entity, CancellationToken ct = default) { Items.Add(entity); return Task.CompletedTask; }
    public void Remove(LakeDetection entity) => Items.Remove(entity);
    public Task<IReadOnlyList<LakeDetection>> ListAsync(int take, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LakeDetection>>(Items.ToList());
    public Task<LakeDetection?> GetByRunIdAsync(Guid runId, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(l => l.RunId == runId));
}

public sealed class FakeScheduler : IBackgroundJobScheduler
{
    public readonly List<(string Id, string Queue, string Job)> Enqueued = new();
    public readonly List<string> Deleted = new();

    public string Enqueue<TJob>(Expression<Func<TJob, Task>> call, string queue = "default")
    {
        string id = "job-" + (Enqueued.Count + 1);
        Enqueued.Add((id, queue, typeof(TJob).Name));
        return id;
    }

    public bool Delete(string jobId) { Deleted.Add(jobId); return true; }
    public void AddOrUpdateRecurring<TJob>(string id, Expression<Func<TJob, Task>> call, string cron) { }
}

public sealed class FakeNotifier : IRunNotifier
{
    public readonly List<string> Events = new();
    public Task ProgressAsync(Guid runId, string stage, double fraction) { Events.Add("progress"); return Task.CompletedTask; }
    public Task StatusAsync(Guid runId, string status) { Events.Add("status:" + status); return Task.CompletedTask; }
    public Task AlertAsync(AlertDto alert) { Events.Add("alert"); return Task.CompletedTask; }
    public Task LakeAsync(LakeDto lake) { Events.Add("lake"); return Task.CompletedTask; }
}

public sealed class FakeEngine : IFloodSimulationEngine
{
    public Task<EngineRunResult> RunAsync(Guid runId, ScenarioSpec spec, IProgress<(string Stage, double Fraction)> progress, CancellationToken ct) =>
        Task.FromResult(new EngineRunResult(new KeyNumbersDto(1, 10, 1, 5, 100, 2, 1, 0, 3, "fake", true), new[] { "fake" },
            new AlertDraft("h", AlertSeverity.Severe, 10, 5)));

    public ScenarioSpec DefaultScenario(Dam dam, IReadOnlyList<Dam> register, bool cascade) =>
        new() { Name = dam.Name + " failure", Dam = new DamSpec { Name = dam.Name, Lon = dam.Lon, Lat = dam.Lat, HeightM = dam.HeightM, ReservoirVolumeM3 = dam.GrossStorageMcm * 1e6 } };

    public string BuildCap(Guid runId, AlertStatus status, bool probabilistic) => "<alert/>";

    public AlertDraft DraftAlert(Guid runId) => new("h", AlertSeverity.Severe, 10, 5);

    public Task<(ScenarioSpec Spec, double LevelM, double VolumeMcm, string Notes)> ScenarioForLakeAsync(LakeDetection lake, CancellationToken ct) =>
        Task.FromResult((new ScenarioSpec
        {
            Name = "Auto lake",
            Mode = ScenarioMode.NaturalDam,
            NaturalDam = new NaturalDamSpec { DebrisPolygon = new() { new[] { 1.0, 1.0 }, new[] { 1.1, 1.0 }, new[] { 1.1, 1.1 } } }
        }, 1000.0, 5.0, "fake estimate"));
}
