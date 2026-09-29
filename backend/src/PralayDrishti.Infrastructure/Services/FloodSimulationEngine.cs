using System.Globalization;
using Microsoft.Extensions.Logging;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Infrastructure.Engine.Export;
using PralayDrishti.Infrastructure.Engine.External;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Hydraulics;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.Infrastructure.Services;

/// <summary>The hydraulics engine as the Application sees it: give it a scenario, get numbers back, results stored.</summary>
public sealed class FloodSimulationEngine : IFloodSimulationEngine
{
    private readonly DemProvider _dem;
    private readonly ExposureProvider _exposure;
    private readonly ResultStore _store;
    private readonly DataPaths _paths;
    private readonly ILogger<FloodSimulationEngine> _log;
    private readonly AppOptions _opt;

    public FloodSimulationEngine(DemProvider dem, ExposureProvider exposure, ResultStore store, DataPaths paths,
        ILogger<FloodSimulationEngine> log, AppOptions? opt = null)
    {
        _dem = dem;
        _exposure = exposure;
        _store = store;
        _paths = paths;
        _log = log;
        _opt = opt ?? new AppOptions();
    }

    public async Task<EngineRunResult> RunAsync(Guid runId, ScenarioSpec spec, IProgress<(string Stage, double Fraction)> progress,
        CancellationToken ct)
    {
        var center = spec.Mode == ScenarioMode.NaturalDam && spec.NaturalDam?.DebrisPolygon.Count > 0
            ? new GeoPoint(spec.NaturalDam.DebrisPolygon.Average(p => p[0]), spec.NaturalDam.DebrisPolygon.Average(p => p[1]))
            : spec.Dam.Location();

        progress.Report(("Fetching terrain", 0.01));
        var dem = await _dem.ForAreaAsync(center, spec.DownstreamKm, spec.UpstreamKm, spec.DemZoom, ct);
        progress.Report(("Loading exposure (settlements, bridges, roads)", 0.04));
        var (exposure, sources) = await _exposure.ForAreaAsync(dem.Dem.Bounds, ct);

        var notes = new List<string> { $"DEM: {dem.Description}" };
        notes.AddRange(sources.Select(s => "Exposure: " + s));
        IFarFieldRunner runner = new InternalSweRunner();
        if (spec.Engine == FarFieldEngine.Delft3D)
        {
            var d3d = new Delft3DFmAdapter(Path.Combine(_paths.Models, runId.ToString("N")), _opt.Delft3DCommand);
            if (d3d.IsConfigured) runner = d3d;
            else notes.Add("Delft3D FM requested but not configured - ran the internal SWE-2D solver; the D-Flow FM model package is under Exports.");
        }

        var engineProgress = new Progress<StageProgress>(sp => progress.Report((sp.Stage, MapStage(sp))));
        var outcome = await Task.Run(() => new ScenarioEngine(runner).Run(spec, dem.Dem, exposure, engineProgress, ct), ct);
        outcome.Prepared.Log.InsertRange(0, notes);

        progress.Report(("Saving results", 0.98));
        _store.Put(runId, outcome);
        _log.LogInformation("Run {Id}: {Area} km2 flooded, {Pop} people at risk", runId, outcome.Impact.FloodedAreaKm2,
            outcome.Impact.PopulationAtRisk);

        return new EngineRunResult(RunDetailBuilder.Numbers(outcome, dem.Description, dem.Synthetic), outcome.Prepared.Log, Draft(outcome));
    }

    // stage weights so one progress bar moves sensibly across the whole chain
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

    private static AlertDraft Draft(ScenarioOutcome o)
    {
        var first = o.Impact.Places.FirstOrDefault(p => p.ArrivalMin != null);
        string where = o.Prepared.Spec.Mode == ScenarioMode.NaturalDam ? "landslide lake" : o.Prepared.Spec.Dam.Name;
        string headline = first == null
            ? $"{where}: no settlement in the modelled flood zone"
            : $"{where}: flood reaches {first.Name} in ~{first.ArrivalMin:F0} min, {o.Impact.PopulationAtRisk:N0} people at risk";
        var severity = o.Impact.Places.Any(p => p.HazardClass >= 5) ? AlertSeverity.Extreme
            : o.Impact.Places.Any(p => p.HazardClass >= 3) ? AlertSeverity.Severe : AlertSeverity.Moderate;
        return new AlertDraft(headline.Length > 200 ? headline[..200] : headline, severity, o.Impact.PopulationAtRisk, first?.ArrivalMin);
    }

    public string BuildCap(Guid runId, AlertStatus status, bool probabilistic)
    {
        var o = _store.Get(runId) ?? throw new NotFoundException("Run results", runId);
        return CapAlertBuilder.Build(o, runId.ToString("N")[..12],
            new CapAlertBuilder.Options(Status: status.ToString(), Probabilistic: probabilistic || o.Ensemble != null));
    }

    public ScenarioSpec DefaultScenario(Dam d, IReadOnlyList<Dam> register, bool cascade)
    {
        var spec = new ScenarioSpec
        {
            Name = $"{d.Name} failure",
            Mode = ScenarioMode.DamBreak,
            Dam = ToDamSpec(d),
            DownstreamKm = d.DownstreamKm ?? 40,
            // big reservoirs drain for hours: longer duration, same grid budget so it stays interactive
            DurationHours = d.GrossStorageMcm > 1000 ? 8 : d.GrossStorageMcm > 100 ? 6 : 4,
            MaxCells = 250_000
        };
        if (cascade && d.CascadeWith.Count > 0)
        {
            var downstream = register.Where(x => d.CascadeWith.Contains(x.Name)).ToList();
            if (downstream.Count > 0)
            {
                spec.Mode = ScenarioMode.Cascade;
                spec.DownstreamDams = downstream.Select(ToDamSpec).ToList();
                spec.Name = $"{d.Name} cascade ({string.Join(", ", downstream.Select(x => x.Name))})";
            }
        }
        return spec;
    }

    private static DamSpec ToDamSpec(Dam d) => new()
    {
        Name = d.Name,
        Lon = d.Lon,
        Lat = d.Lat,
        Type = d.Type,
        HeightM = d.HeightM,
        ReservoirVolumeM3 = d.GrossStorageMcm * 1e6,
        CrestLengthM = d.CrestLengthM > 0 ? d.CrestLengthM : 300,
        CrestWidthM = d.CrestWidthM > 0 ? d.CrestWidthM : 10,
        Dykes = d.Dykes
    };

    public async Task<(ScenarioSpec Spec, double LevelM, double VolumeMcm, string Notes)> ScenarioForLakeAsync(LakeDetection lake,
        CancellationToken ct)
    {
        var point = new GeoPoint(lake.Lon, lake.Lat);
        var src = await _dem.ForAreaAsync(point, 30, 5, _opt.DefaultDemZoom, ct);
        var dem = src.Dem;
        var est = SatelliteReservoirEstimator.Estimate(dem, new SatelliteReservoirEstimator.Request(point, lake.AreaKm2, MaxRise: 150));

        // the blockage sits at the lake outlet: the edge cell draining out of the lake with the largest upstream area
        var routing = Hydrology.Route(dem);
        var inLake = new HashSet<int>(est.LakeCells);
        int outlet = -1;
        foreach (int i in est.LakeCells)
        {
            int j = routing.Receivers[i];
            if (j < 0 || inLake.Contains(j)) continue;
            if (outlet < 0 || routing.Accumulation[i] > routing.Accumulation[outlet]) outlet = i;
        }
        if (outlet < 0) throw new BusinessRuleException("Could not locate the lake outlet on the DEM.");
        var c = dem.CellCenter(routing.Receivers[outlet]);
        double half = Math.Max(dem.Dx, 60) * 2.5;
        var ring = new List<double[]>
        {
            new[] { c.Offset(-half, -half).Lon, c.Offset(-half, -half).Lat },
            new[] { c.Offset(half, -half).Lon, c.Offset(half, -half).Lat },
            new[] { c.Offset(half, half).Lon, c.Offset(half, half).Lat },
            new[] { c.Offset(-half, half).Lon, c.Offset(-half, half).Lat }
        };

        var spec = new ScenarioSpec
        {
            Name = string.Create(CultureInfo.InvariantCulture, $"Auto: new lake {lake.Lat:F3}N {lake.Lon:F3}E ({lake.AreaKm2:F2} km2)"),
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
        return (spec, Math.Round(est.EstimatedLevel, 1), Math.Round(est.EstimatedVolumeM3 / 1e6, 3),
            string.Join(" ", est.Notes) + $" DEM: {src.Description}.");
    }
}
