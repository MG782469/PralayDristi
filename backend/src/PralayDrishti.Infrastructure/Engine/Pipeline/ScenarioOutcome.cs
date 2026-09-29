using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Hydraulics;
using PralayDrishti.Infrastructure.Engine.Solvers;

namespace PralayDrishti.Infrastructure.Engine.Pipeline;

public interface IFarFieldRunner
{
    string Name { get; }
    SweResult Run(SweSetup setup, IProgress<double>? progress, CancellationToken ct);
}

public sealed class InternalSweRunner : IFarFieldRunner
{
    public string Name => "PralayDrishti SWE-2D (HLL, hydrostatic reconstruction)";
    public SweResult Run(SweSetup setup, IProgress<double>? progress, CancellationToken ct) =>
        new SweSolver(setup).Run(progress, ct);
}

public sealed record EmpiricalPeak(string Method, double PeakM3s);

public sealed record CascadeEvent(string Dam, double? FailedAtMin, double ReservoirVolumeM3, double CrestElevation);

/// <summary>Everything the pipeline needs to re-run the far field quickly (inverse queries, ensembles).</summary>
public sealed class PreparedScenario
{
    public required ScenarioSpec Spec { get; init; }
    public required DemGrid Dem { get; init; }                // conditioned grid (dams, burned channel)
    public required DemGrid OriginalDem { get; init; }        // same grid before any conditioning
    public required GeoPoint DamPoint { get; init; }
    public required int DamCell { get; init; }
    public required List<int> DownstreamPath { get; init; }
    public required List<int> UpstreamPath { get; init; }
    public required int[] InflowCells { get; init; }
    public required (double east, double north) FlowDir { get; init; }
    public required BreachGeometry Breach { get; init; }
    public required Hydrograph Hydrograph { get; set; }
    public required Hydrograph RoutedHydrograph { get; init; }
    public required StorageCurve Storage { get; init; }
    public required double BedAtDam { get; init; }
    public required float[] ManningField { get; init; }
    public required bool[] ExcludeMask { get; init; }
    public List<EmpiricalPeak> EmpiricalPeaks { get; init; } = new();
    public NaturalDamResult? NaturalDam { get; init; }
    public List<DamStructure> Structures { get; init; } = new();
    public float[]? InitialDepth { get; init; }
    public List<string> Log { get; init; } = new();
}

public sealed class ScenarioOutcome
{
    public required PreparedScenario Prepared { get; init; }
    public required SweResult FarField { get; init; }
    public required ImpactReport Impact { get; init; }
    public byte[] HazardClass { get; init; } = Array.Empty<byte>();
    public NearFieldResult? NearField { get; init; }
    public EnsembleResult? Ensemble { get; init; }
    public List<CascadeEvent> Cascade { get; init; } = new();
    public string EngineName { get; init; } = "";
    public Dictionary<string, double> TimingsSec { get; init; } = new();
}
