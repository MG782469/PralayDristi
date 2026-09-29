using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.EngineTests;

/// <summary>Regressions for the three engine problems found on real dams (Tehri, Hirakud, peak mismatch).</summary>
public class EngineFixTests
{
    private const double Cell = 30;

    /// <summary>
    /// A steep tributary (column 100) running south into a big flat reservoir on the main river (rows 140..160).
    /// The reservoir cells carry far more accumulation, which is exactly what used to win the snap.
    /// </summary>
    private static DemGrid TributaryIntoReservoir()
    {
        double deg = Cell / 110574.0;
        var dem = new DemGrid(200, 200, 80.0, 30.0, deg);
        for (int r = 0; r < 200; r++)
            for (int c = 0; c < 200; c++)
            {
                double z;
                if (r < 140) z = 505 + (140 - r) * 1.4 + Math.Abs(c - 100) * 1.0;
                else if (r <= 160) z = 500;
                else z = 500 + (r - 160) * 2.0;
                dem[r, c] = (float)z;
            }
        return dem;
    }

    private static double DropWithin(DemGrid dem, int[] receivers, int start, double metres)
    {
        var path = Hydrology.TraceDownstream(dem, receivers, start, metres);
        return dem.Z[start] - path.Min(i => dem.Z[i]);
    }

    [Fact]
    public void Snap_rejects_reservoir_cell_and_picks_descending_channel()
    {
        var dem = TributaryIntoReservoir();
        var routing = Hydrology.Route(dem);
        var damPoint = dem.CellCenter(132, 100);
        var log = new List<string>();

        int cell = ScenarioEngine.SnapDam(dem, routing, damPoint, log);

        Assert.True(dem.Z[cell] > 500.5, $"snapped onto the reservoir surface ({dem.Z[cell]} m)");
        Assert.True(DropWithin(dem, routing.Receivers, cell, 2000) >= 10);
    }

    [Fact]
    public void Wall_spans_crest_length()
    {
        // 12 km wide, dead flat across the valley: nothing stops the wall except its half-length
        double deg = Cell / 110574.0;
        var dem = new DemGrid(300, 400, 80.0, 25.0, deg);
        for (int r = 0; r < 400; r++)
            for (int c = 0; c < 300; c++)
                dem[r, c] = (float)(500 - 0.005 * c * Cell - (r == 200 ? 3 : 0));
        var site = dem.CellCenter(200, 60);
        var spec = new ScenarioSpec
        {
            Name = "long wall",
            Dam = new DamSpec { Name = "Hirakud-like", Lon = site.Lon, Lat = site.Lat, HeightM = 20, ReservoirVolumeM3 = 50e6, CrestLengthM = 6000, Type = DamType.Earthfill },
            MaxCells = 200_000,
            ChannelBurnM = 0
        };

        var prep = new ScenarioEngine().Prepare(spec, dem);

        int wallCells = prep.ExcludeMask.Count(x => x);
        Assert.True(wallCells * prep.Dem.Dy >= 2 * 5500, $"wall only {wallCells * prep.Dem.Dy:F0} m long");
    }

    private static ScenarioSpec ValleySpec(SyntheticTerrain.Valley v, Action<DamSpec>? tweak = null, bool calibrate = false)
    {
        var dam = new DamSpec { Name = "Test dam", Lon = v.DamSite.Lon, Lat = v.DamSite.Lat, HeightM = 40, ReservoirVolumeM3 = 20e6, Type = DamType.Earthfill };
        tweak?.Invoke(dam);
        return new ScenarioSpec { Name = "calib", Dam = dam, CalibrateBreachToEmpirical = calibrate };
    }

    [Fact]
    public void Calibration_brings_routed_peak_within_10_percent_of_empirical()
    {
        var v = SyntheticTerrain.Build(new GeoPoint(79.6, 30.5));

        var prep = new ScenarioEngine().Prepare(ValleySpec(v, calibrate: true), v.Dem);

        double target = prep.EmpiricalPeaks[0].PeakM3s;
        Assert.InRange(prep.RoutedHydrograph.Peak, 0.9 * target, 1.1 * target);
        Assert.Contains(prep.Log, l => l.Contains("calibrated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Large_peak_mismatch_logs_warning()
    {
        var v = SyntheticTerrain.Build(new GeoPoint(79.6, 30.5));

        // a breach 1.5 km wide empties the reservoir far faster than any regression predicts
        var prep = new ScenarioEngine().Prepare(ValleySpec(v, d => d.BreachWidthM = 1500), v.Dem);

        Assert.True(prep.Log.Any(l => l.StartsWith("Warning") && l.Contains("empirical")), string.Join(" | ", prep.Log) + " peaks " + string.Join(",", prep.EmpiricalPeaks));
    }
}
