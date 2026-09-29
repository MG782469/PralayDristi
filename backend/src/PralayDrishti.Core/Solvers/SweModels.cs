using PralayDrishti.Core.Geo;
using PralayDrishti.Core.Hydraulics;

namespace PralayDrishti.Core.Solvers;

/// <summary>Discharge injected into a set of cells (breach outlet when the reservoir is outside the grid).</summary>
public sealed class InflowSource
{
    public required int[] Cells { get; init; }
    public required Hydrograph Hydrograph { get; init; }
    public double DirEast { get; init; } = 1;
    public double DirNorth { get; init; }
}

/// <summary>
/// A dam that lives inside the grid: its crest is burnt into the bed, a reservoir sits behind it and it
/// fails (bed lowered over tf) either at a fixed time or once it is overtopped. This is what makes
/// cascade failures possible without special casing anything in the solver.
/// </summary>
public sealed class DamStructure
{
    public required string Name { get; init; }
    public required int[] Cells { get; init; }
    public required int[] BreachCells { get; init; }
    public required float[] OriginalBed { get; init; }     // aligned with BreachCells
    public required double Crest { get; init; }
    public required BreachGeometry Breach { get; init; }
    public int[] ProbeCells { get; init; } = Array.Empty<int>();
    public double OvertopTrigger { get; init; } = 0.3;
    public double? ForcedFailureTime { get; init; }

    public double? FailedAt { get; internal set; }
}

public sealed class Gauge
{
    public required string Name { get; init; }
    public required int Cell { get; init; }
    public List<float> Time { get; } = new();
    public List<float> Depth { get; } = new();
    public List<float> Speed { get; } = new();
    public List<float> Surface { get; } = new();
}

public sealed class SweSetup
{
    public required DemGrid Dem { get; init; }
    public double Manning { get; init; } = 0.035;
    public float[]? ManningField { get; init; }
    public List<InflowSource> Inflows { get; init; } = new();
    public List<DamStructure> Structures { get; init; } = new();
    public List<Gauge> Gauges { get; init; } = new();
    public float[]? InitialDepth { get; init; }
    public double Duration { get; init; } = 6 * 3600;
    public double SnapshotInterval { get; init; } = 600;
    public double GaugeInterval { get; init; } = 30;
    public double Cfl { get; init; } = 0.45;
    public double ArrivalDepth { get; init; } = 0.1;
    public double MaxDt { get; init; } = 10;
    public bool KeepSnapshots { get; init; } = true;
}

public sealed record Snapshot(double Time, float[] Depth);

public sealed class SweResult
{
    public required DemGrid Dem { get; init; }            // bed actually used (with dams / burned channel)
    public required float[] MaxDepth { get; init; }
    public required float[] MaxSpeed { get; init; }
    public required float[] MaxDv { get; init; }
    public required float[] ArrivalTime { get; init; }    // s, NaN = never wet
    public required float[] PeakTime { get; init; }
    public required float[] MaxSurface { get; init; }
    public List<Snapshot> Snapshots { get; init; } = new();
    public List<Gauge> Gauges { get; init; } = new();
    public List<(string name, double time)> Failures { get; init; } = new();
    public double InflowVolume { get; init; }
    public double InitialVolume { get; init; }
    public double FinalVolume { get; init; }
    public double OutflowVolume { get; init; }
    public long Steps { get; init; }
    public double SimulatedTime { get; init; }
    public TimeSpan WallTime { get; init; }

    public double MassError =>
        Math.Abs(InitialVolume + InflowVolume - OutflowVolume - FinalVolume) / Math.Max(1, InitialVolume + InflowVolume);
}
