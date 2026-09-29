using PralayDrishti.Core.Solvers;
using Xunit.Abstractions;

namespace PralayDrishti.Tests;

public class SphTests
{
    private readonly ITestOutputHelper _out;
    public SphTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Flat_bed_dam_break_front_and_depth_are_reasonable()
    {
        var setup = new SphSetup
        {
            ProfileX = new[] { -5.0, 15.0 },
            ProfileZ = new[] { 0.0, 0.0 },
            ReservoirDepth = 1,
            ReservoirLength = 2,
            DomainEnd = 8,
            ParticlesOverDepth = 20,
            Duration = 1.0,
            GaugeX = new[] { 0.0, 1.0, 2.0 }
        };
        var res = new SphSolver(setup).Run();
        double front = res.FrontX[^1];
        double tEnd = res.FrontTime[^1];
        _out.WriteLine($"particles {res.FluidParticles}, steps {res.Steps}, wall {res.WallTime.TotalSeconds:F1}s");
        _out.WriteLine($"front at t={tEnd:F2}: {front:F2} (Ritter 2c0 t = {2 * Math.Sqrt(9.81) * tEnd:F2})");
        foreach (var g in res.Gauges)
            _out.WriteLine($"gauge {g.X}: depth@end {g.Depth[^1]:F3} ritter {AnalyticDamBreak.Ritter(1, g.X, g.Time[^1]).h:F3}, max {g.Depth.Max():F3}");

        // SPH fronts are slower than Ritter (no friction there, but the tip is thin and resolution-limited);
        // literature (e.g. DualSPHysics validations) gets 70-95% of the ideal front speed
        double ideal = 2 * Math.Sqrt(9.81) * tEnd;
        Assert.InRange(front, 0.5 * ideal, 1.05 * ideal);
        // at the dam section the Ritter depth is 4/9 h0 for all t
        Assert.InRange(res.Gauges[0].Depth[^1], 0.3, 0.6);
    }
}
