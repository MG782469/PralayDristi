using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Hydraulics;
using PralayDrishti.Infrastructure.Engine.Solvers;
using Xunit.Abstractions;

namespace PralayDrishti.EngineTests;

public class SolverTests
{
    private readonly ITestOutputHelper _out;
    public SolverTests(ITestOutputHelper output) => _out = output;

    private static (SweResult res, DemGrid dem, double x0) RunFlumeDamBreak(double h0, double h1, double t, double dx = 1.0)
    {
        int nx = 400;
        var dem = SyntheticTerrain.Flume(nx, 3, dx);
        double x0 = nx / 2 * dem.Dx;
        var init = new float[dem.Count];
        for (int c = 0; c < nx; c++)
            for (int r = 0; r < 3; r++)
                init[dem.Idx(r, c)] = (float)((c + 0.5) * dem.Dx < x0 ? h0 : h1);
        var setup = new SweSetup
        {
            Dem = dem, Manning = 0, InitialDepth = init, Duration = t, SnapshotInterval = t, MaxDt = 0.05
        };
        return (new SweSolver(setup).Run(), dem, x0);
    }

    [Fact]
    public void Ritter_dry_bed_matches_analytic()
    {
        double h0 = 10, t = 8;
        var (res, dem, x0) = RunFlumeDamBreak(h0, 0, t);
        var final = res.Snapshots[^1].Depth;
        double err = 0, norm = 0;
        for (int c = 0; c < dem.Cols; c++)
        {
            double x = (c + 0.5) * dem.Dx - x0;
            double exact = AnalyticDamBreak.Ritter(h0, x, res.SimulatedTime).h;
            err += Math.Abs(final[dem.Idx(1, c)] - exact);
            norm += exact;
        }
        double rel = err / norm;
        _out.WriteLine($"Ritter relative L1 = {rel:P2}, steps {res.Steps}");
        Assert.True(rel < 0.04, $"relative L1 error {rel:P2}");
    }

    [Fact]
    public void Stoker_wet_bed_matches_analytic()
    {
        double h0 = 10, h1 = 2, t = 8;
        var (res, dem, x0) = RunFlumeDamBreak(h0, h1, t);
        var final = res.Snapshots[^1].Depth;
        double err = 0, norm = 0;
        for (int c = 0; c < dem.Cols; c++)
        {
            double x = (c + 0.5) * dem.Dx - x0;
            double exact = AnalyticDamBreak.Stoker(h0, h1, x, res.SimulatedTime).h;
            err += Math.Abs(final[dem.Idx(1, c)] - exact);
            norm += exact;
        }
        double rel = err / norm;
        _out.WriteLine($"Stoker relative L1 = {rel:P2}");
        Assert.True(rel < 0.03);
    }

    [Fact]
    public void Lake_at_rest_stays_at_rest()
    {
        // well-balanced check: still water over a bump must not start moving
        var dem = SyntheticTerrain.Flume(60, 60, 5);
        var init = new float[dem.Count];
        for (int r = 0; r < 60; r++)
            for (int c = 0; c < 60; c++)
            {
                double d = Math.Sqrt((r - 30) * (r - 30) + (c - 30) * (c - 30));
                dem[r, c] = (float)Math.Max(0, 4 - d * 0.3);
                init[dem.Idx(r, c)] = Math.Max(0, 5 - dem[r, c]);
            }
        var res = new SweSolver(new SweSetup { Dem = dem, InitialDepth = init, Duration = 60, KeepSnapshots = false }).Run();
        Assert.True(res.MaxSpeed.Max() < 1e-6, $"spurious velocity {res.MaxSpeed.Max()}");
    }

    [Fact]
    public void Inflow_mass_is_conserved()
    {
        var valley = SyntheticTerrain.Build(new GeoPoint(79.6, 30.5), cols: 160, rows: 90);
        var routing = Hydrology.Route(valley.Dem);
        int dam = Hydrology.SnapToRiver(valley.Dem, routing.Accumulation, valley.DamSite);
        var hyd = new Hydrograph();
        hyd.Add(0, 0); hyd.Add(300, 2000); hyd.Add(1200, 0);
        var res = new SweSolver(new SweSetup
        {
            Dem = valley.Dem,
            Inflows = { new InflowSource { Cells = new[] { dam }, Hydrograph = hyd } },
            Duration = 1800,
            KeepSnapshots = false
        }).Run();
        _out.WriteLine($"inflow {res.InflowVolume:E3} final {res.FinalVolume:E3} out {res.OutflowVolume:E3} err {res.MassError:P3}");
        Assert.True(res.MassError < 0.005);
        Assert.True(res.MaxDepth.Max() > 1);
    }
}
