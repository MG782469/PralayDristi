using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Hydraulics;
using PralayDrishti.Infrastructure.Engine.Pipeline;
using Xunit.Abstractions;

namespace PralayDrishti.EngineTests;

public class PipelineTests
{
    private readonly ITestOutputHelper _out;
    public PipelineTests(ITestOutputHelper o) => _out = o;

    private static ExposureSet VillagesAlong(SyntheticTerrain.Valley v)
    {
        var set = new ExposureSet();
        int[] at = { 80, 150, 220, 300 };
        for (int k = 0; k < at.Length; k++)
            set.Points.Add(new ExposurePoint($"Gaon-{k + 1}", AssetKind.Village, v.RiverLine[at[k]].Offset(0, 40), 500 * (k + 1)));
        set.Points.Add(new ExposurePoint("Pul-1", AssetKind.Bridge, v.RiverLine[120], DeckClearance: 6));
        set.Points.Add(new ExposurePoint("PHC", AssetKind.Hospital, v.RiverLine[260].Offset(0, 250)));
        set.Roads.Add(new ExposureLine("NH-X", "primary", v.RiverLine.Where((_, i) => i % 10 == 0).Select(p => p.Offset(0, 120)).ToList()));
        return set;
    }

    [Fact]
    public void Dam_break_end_to_end_with_sph()
    {
        var v = SyntheticTerrain.Build(new GeoPoint(79.6, 30.5));
        var spec = new ScenarioSpec
        {
            Name = "synthetic",
            Dam = new DamSpec { Name = "Test dam", Lon = v.DamSite.Lon, Lat = v.DamSite.Lat, HeightM = 40, ReservoirVolumeM3 = 20e6, Type = DamType.Earthfill },
            DurationHours = 1.5,
            RunSph = true,
            SphResolution = 16,
            Structure = new StructureSpec { Name = "Barrage", DistanceKm = 0.3, HeightM = 10, WidthM = 40 },
            EnsembleMembers = 3
        };
        var outcome = new ScenarioEngine().Run(spec, v.Dem, VillagesAlong(v));
        foreach (var l in outcome.Prepared.Log) _out.WriteLine(l);
        foreach (var t in outcome.TimingsSec) _out.WriteLine($"{t.Key}: {t.Value:F1}s");
        var imp = outcome.Impact;
        _out.WriteLine($"area {imp.FloodedAreaKm2} km2, maxDepth {imp.MaxDepth}, pop {imp.PopulationAtRisk}, loss {imp.Loss.TotalCrore:F1} cr");
        foreach (var p in imp.Places) _out.WriteLine($"  {p.Name} depth {p.MaxDepth} arr {p.ArrivalMin} golden {p.GoldenTimeMin} {p.HazardLabel}");
        foreach (var b in imp.Bridges) _out.WriteLine($"  bridge {b.Name} over={b.Overtopped} at {b.OvertopMin}");
        foreach (var s in imp.Staging) _out.WriteLine($"  {s.Type} {s.AreaHa} ha score {s.Score}");
        foreach (var r in imp.Recommendations) _out.WriteLine($"  * {r}");
        var nf = outcome.NearField!;
        _out.WriteLine($"SPH particles {nf.Particles}, {nf.SphWallSeconds:F1}s, force {nf.PeakImpactForceKN:F0} kN, q_sph {nf.SphDamUnitDischargePeak:F0} vs ritter {nf.RitterDamUnitDischarge:F0}");
        foreach (var g in nf.Gauges) _out.WriteLine($"  gauge {g.DistanceM:F0}m sph {g.SphPeakDepth:F1} swe {g.SwePeakDepth:F1} arr {g.SphArrival:F0}/{g.SweArrival:F0}");

        Assert.True(imp.FloodedAreaKm2 > 0.5);
        Assert.True(imp.Places.Count >= 2);
        Assert.True(outcome.FarField.MassError < 0.01);
        Assert.NotNull(outcome.Ensemble);
        Assert.True(nf.PeakImpactForceKN > 0);
    }

    [Fact]
    public void Natural_dam_mode_estimates_lake_and_floods_downstream()
    {
        var v = SyntheticTerrain.Build(new GeoPoint(79.6, 30.5));
        var c = v.RiverLine[120];
        // debris polygon across the valley
        var poly = new List<double[]>
        {
            new[] { c.Offset(-60, -400).Lon, c.Offset(-60, -400).Lat },
            new[] { c.Offset(60, -400).Lon, c.Offset(60, -400).Lat },
            new[] { c.Offset(60, 400).Lon, c.Offset(60, 400).Lat },
            new[] { c.Offset(-60, 400).Lon, c.Offset(-60, 400).Lat },
        };
        var spec = new ScenarioSpec
        {
            Mode = ScenarioMode.NaturalDam,
            NaturalDam = new NaturalDamSpec { DebrisPolygon = poly, HeightAboveBed = 35 },
            DurationHours = 1,
            RunSph = false
        };
        var outcome = new ScenarioEngine().Run(spec, v.Dem, VillagesAlong(v));
        var nat = outcome.Prepared.NaturalDam!;
        _out.WriteLine($"lake {nat.LakeVolumeM3 / 1e6:F2} MCM area {nat.LakeAreaM2 / 1e6:F2} km2 height {nat.DamHeight:F1}");
        foreach (var l in outcome.Prepared.Log) _out.WriteLine(l);
        Assert.InRange(nat.DamHeight, 30, 40);
        Assert.True(nat.LakeVolumeM3 > 1e6);
        Assert.True(outcome.Impact.FloodedAreaKm2 > 0.2);
    }

    [Fact]
    public void Cascade_downstream_dam_fails_when_overtopped()
    {
        var v = SyntheticTerrain.Build(new GeoPoint(79.6, 30.5));
        var d2 = v.RiverLine[150];
        var spec = new ScenarioSpec
        {
            Mode = ScenarioMode.Cascade,
            Dam = new DamSpec { Name = "Upper", Lon = v.DamSite.Lon, Lat = v.DamSite.Lat, HeightM = 60, ReservoirVolumeM3 = 60e6 },
            DownstreamDams = { new DamSpec { Name = "Lower", Lon = d2.Lon, Lat = d2.Lat, HeightM = 15, WaterDepthM = 12, ReservoirVolumeM3 = 1e6 } },
            DurationHours = 1.5,
            RunSph = false
        };
        var outcome = new ScenarioEngine().Run(spec, v.Dem, VillagesAlong(v));
        foreach (var l in outcome.Prepared.Log) _out.WriteLine(l);
        foreach (var f in outcome.Cascade) _out.WriteLine($"{f.Dam} failed at {f.FailedAtMin} min");
        Assert.Single(outcome.Cascade);
        Assert.NotNull(outcome.Cascade[0].FailedAtMin);
    }
}
