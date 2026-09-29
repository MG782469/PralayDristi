using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PralayDrishti.API.Data;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.API.Services;

/// <summary>
/// India Dam Risk Atlas: the same pipeline in batch over every dam in the register, on a coarse grid, then
/// ranked by population at risk. Proof that the framework is generalised, not tuned for one valley.
/// </summary>
public sealed class AtlasService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly RunQueue _queue;

    public AtlasService(IServiceScopeFactory scopes, RunQueue queue)
    {
        _scopes = scopes;
        _queue = queue;
    }

    public async Task<List<Guid>> QueueAllAsync(IEnumerable<int>? damIds, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dams = await db.Dams.ToListAsync(ct);
        if (damIds != null)
        {
            var set = damIds.ToHashSet();
            dams = dams.Where(d => set.Contains(d.Id)).ToList();
        }
        var ids = new List<Guid>();
        foreach (var d in dams)
        {
            var spec = ScenarioFactory.Default(d, dams);
            spec.Name = $"Atlas: {d.Name}";
            spec.MaxCells = 70_000;
            spec.RunSph = false;
            spec.EnsembleMembers = 0;
            spec.DurationHours = Math.Min(spec.DurationHours, 4);
            spec.DemZoom = 11;
            var run = ScenarioFactory.NewRun(spec, "atlas", d.Id);
            db.Runs.Add(run);
            ids.Add(run.Id);
        }
        await db.SaveChangesAsync(ct);
        foreach (var id in ids) await _queue.EnqueueAsync(id);
        return ids;
    }

    public async Task<List<object>> LeaderboardAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dams = await db.Dams.ToDictionaryAsync(d => d.Id, ct);
        var runs = await db.Runs.Where(r => r.Origin == "atlas" && r.Status == RunStatus.Completed)
            .OrderByDescending(r => r.FinishedUtc).ToListAsync(ct);
        return runs.GroupBy(r => r.DamId)
            .Select(g => g.First())
            .Where(r => r.DamId != null && dams.ContainsKey(r.DamId.Value))
            .Select(r =>
            {
                var n = JsonSerializer.Deserialize<KeyNumbers>(r.SummaryJson ?? "{}", RunArchive.Json);
                var d = dams[r.DamId!.Value];
                return new
                {
                    runId = r.Id, damId = d.Id, dam = d.Name, river = d.River, state = d.State, d.Lat, d.Lon,
                    population = n?.PopulationAtRisk ?? 0, settlements = n?.Settlements ?? 0, areaKm2 = n?.FloodedAreaKm2 ?? 0,
                    firstArrivalMin = n?.FirstArrivalMin, peakQ = n?.PeakDischarge ?? 0, lossCrore = n?.LossCrore ?? 0,
                    bridges = n?.BridgesOvertopped ?? 0, synthetic = n?.SyntheticDem ?? false
                };
            })
            .OrderByDescending(x => x.population)
            .Cast<object>()
            .ToList();
    }
}
