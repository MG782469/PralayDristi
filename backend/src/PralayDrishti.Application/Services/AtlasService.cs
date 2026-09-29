using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Services;

public sealed record LeaderboardRowDto(
    Guid RunId, int DamId, string Dam, string River, string State, double Lat, double Lon, int Population, int Settlements,
    double AreaKm2, double? FirstArrivalMin, double PeakQ, double LossCrore, int Bridges, bool Synthetic);

/// <summary>
/// India Dam Risk Atlas: the same pipeline in batch over the whole register on a coarse grid, ranked by people at
/// risk. Proof that the framework is general, not tuned for one valley.
/// </summary>
public sealed class AtlasService
{
    private readonly IDamRepository _dams;
    private readonly ISimulationRunRepository _runs;
    private readonly IFloodSimulationEngine _engine;
    private readonly RunFactory _factory;

    public AtlasService(IDamRepository dams, ISimulationRunRepository runs, IFloodSimulationEngine engine, RunFactory factory)
    {
        _dams = dams;
        _runs = runs;
        _engine = engine;
        _factory = factory;
    }

    public async Task<IReadOnlyList<Guid>> QueueAsync(IReadOnlyList<int>? damIds, CancellationToken ct)
    {
        var all = await _dams.ListAsync(ct);
        var chosen = damIds is { Count: > 0 } ? all.Where(d => damIds.Contains(d.Id)).ToList() : all.ToList();
        var ids = new List<Guid>();
        foreach (var d in chosen)
        {
            var spec = _engine.DefaultScenario(d, all, cascade: false);
            spec.Name = $"Atlas: {d.Name}";
            spec.MaxCells = 70_000;
            spec.RunSph = false;
            spec.EnsembleMembers = 0;
            spec.DurationHours = Math.Min(spec.DurationHours, 4);
            spec.DemZoom = 11;
            ids.Add((await _factory.QueueAsync(spec, RunOrigin.Atlas, d.Id, null, ct)).Id);
        }
        return ids;
    }

    public async Task<IReadOnlyList<LeaderboardRowDto>> LeaderboardAsync(CancellationToken ct)
    {
        var dams = (await _dams.ListAsync(ct)).ToDictionary(d => d.Id);
        var runs = await _runs.LatestCompletedPerDamAsync(RunOrigin.Atlas, ct);
        return runs
            .Where(r => r.DamId is int id && dams.ContainsKey(id))
            .Select(r =>
            {
                var n = r.ToDto().Numbers;
                var d = dams[r.DamId!.Value];
                return new LeaderboardRowDto(r.Id, d.Id, d.Name, d.River, d.State, d.Lat, d.Lon, n?.PopulationAtRisk ?? 0,
                    n?.Settlements ?? 0, n?.FloodedAreaKm2 ?? 0, n?.FirstArrivalMin, n?.PeakDischarge ?? 0, n?.LossCrore ?? 0,
                    n?.BridgesOvertopped ?? 0, n?.SyntheticDem ?? false);
            })
            .OrderByDescending(x => x.Population)
            .ToList();
    }
}
