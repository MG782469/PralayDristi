using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Hydraulics;

namespace PralayDrishti.Infrastructure.Engine.Pipeline;

public enum ScenarioMode { DamBreak, NaturalDam, Cascade }

public enum FarFieldEngine { Internal, Delft3D }

public sealed class DamSpec
{
    public string Name { get; set; } = "Dam";
    public double Lon { get; set; }
    public double Lat { get; set; }
    public DamType Type { get; set; } = DamType.Earthfill;
    public double HeightM { get; set; } = 50;
    public double? WaterDepthM { get; set; }             // default: 0.95 x height
    public double ReservoirVolumeM3 { get; set; } = 50e6;
    public double CrestLengthM { get; set; } = 300;
    public double CrestWidthM { get; set; } = 10;
    public FailureMode FailureMode { get; set; } = FailureMode.Overtopping;
    public BreachMethod? BreachMethod { get; set; }
    public double? BreachWidthM { get; set; }            // user override
    public double? FormationTimeMin { get; set; }        // user override
    public double StorageExponent { get; set; } = 2.5;

    public GeoPoint Location => new(Lon, Lat);
    public double HwOrDefault => WaterDepthM ?? HeightM * 0.95;
}

public sealed class NaturalDamSpec
{
    public List<double[]> DebrisPolygon { get; set; } = new();   // [lon, lat]
    public double? CrestElevation { get; set; }
    public double? HeightAboveBed { get; set; }
    public double? CurrentLakeLevel { get; set; }
    public DamType Type { get; set; } = DamType.LandslideDam;

    public List<GeoPoint> Ring => DebrisPolygon.Select(p => new GeoPoint(p[0], p[1])).ToList();
}

public sealed class StructureSpec
{
    public string Name { get; set; } = "Barrage";
    public double DistanceKm { get; set; } = 3;
    public double HeightM { get; set; } = 15;
    public double WidthM { get; set; } = 60;
}

public sealed class ScenarioSpec
{
    public string Name { get; set; } = "Scenario";
    public ScenarioMode Mode { get; set; } = ScenarioMode.DamBreak;
    public DamSpec Dam { get; set; } = new();
    public NaturalDamSpec? NaturalDam { get; set; }
    public List<DamSpec> DownstreamDams { get; set; } = new();

    public double DurationHours { get; set; } = 6;
    public double Manning { get; set; } = 0.045;
    public double ChannelManning { get; set; } = 0.035;
    public double ChannelBurnM { get; set; } = 1.5;
    public int MaxCells { get; set; } = 250_000;
    public double DownstreamKm { get; set; } = 40;
    public double UpstreamKm { get; set; } = 8;
    public FarFieldEngine Engine { get; set; } = FarFieldEngine.Internal;

    public bool RunSph { get; set; } = true;
    public bool SphHandover { get; set; }                 // use SPH discharge as inflow start (instantaneous failures)
    public int SphResolution { get; set; } = 24;
    public bool DebrisFlow { get; set; }
    public StructureSpec? Structure { get; set; }

    public int EnsembleMembers { get; set; }
    public int EnsembleSeed { get; set; } = 42;

    public double ArrivalDepth { get; set; } = 0.3;
    public double WarningMinutes { get; set; } = 15;
    public int DemZoom { get; set; } = 12;
}
