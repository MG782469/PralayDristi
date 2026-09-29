namespace PralayDrishti.Core.Analysis;

public sealed record InverseStep(double Factor, double Value, bool Satisfied);

public sealed record InverseResult(
    bool Found,
    double Factor,
    double? ValueAtFactor,
    List<InverseStep> Trace,
    string Message);

/// <summary>
/// The "ulta sawaal" queries. Both are 1-D searches over a scale factor applied to the release hydrograph,
/// with the flood model in the loop (coarse grid, a few seconds per evaluation):
///  - threshold: smallest release that floods a target (bridge deck, army camp, highway) to a given depth
///  - back-calculation: which release best explains a flood extent seen on satellite (max CSI)
/// </summary>
public static class InverseSolver
{
    public static InverseResult Threshold(Func<double, double> depthAtTarget, double requiredDepth,
        double lo = 0.02, double hi = 1.0, int iterations = 8)
    {
        var trace = new List<InverseStep>();
        double dHi = depthAtTarget(hi);
        trace.Add(new InverseStep(hi, dHi, dHi >= requiredDepth));
        if (dHi < requiredDepth)
            return new InverseResult(false, hi, dHi, trace,
                $"Even the full release only gives {dHi:F2} m at the target - it will not reach {requiredDepth:F2} m.");

        double dLo = depthAtTarget(lo);
        trace.Add(new InverseStep(lo, dLo, dLo >= requiredDepth));
        if (dLo >= requiredDepth)
            return new InverseResult(true, lo, dLo, trace,
                $"Target floods even at {lo:P0} of the release - practically any failure reaches it.");

        double best = hi, bestVal = dHi;
        for (int i = 0; i < iterations; i++)
        {
            // geometric bisection - releases span orders of magnitude
            double mid = Math.Sqrt(lo * hi);
            double d = depthAtTarget(mid);
            bool ok = d >= requiredDepth;
            trace.Add(new InverseStep(mid, d, ok));
            if (ok) { hi = mid; best = mid; bestVal = d; }
            else lo = mid;
        }
        return new InverseResult(true, best, bestVal, trace,
            $"About {best:P0} of the full release floods the target to {requiredDepth:F2} m.");
    }

    public static InverseResult BestFit(Func<double, double> csiForFactor, double lo = 0.05, double hi = 1.5, int iterations = 10)
    {
        var trace = new List<InverseStep>();
        const double gr = 0.6180339887;
        double a = lo, b = hi;
        double c = b - gr * (b - a), d = a + gr * (b - a);
        double fc = csiForFactor(c), fd = csiForFactor(d);
        trace.Add(new InverseStep(c, fc, false));
        trace.Add(new InverseStep(d, fd, false));
        for (int i = 0; i < iterations; i++)
        {
            if (fc > fd)
            {
                b = d; d = c; fd = fc;
                c = b - gr * (b - a);
                fc = csiForFactor(c);
                trace.Add(new InverseStep(c, fc, false));
            }
            else
            {
                a = c; c = d; fc = fd;
                d = a + gr * (b - a);
                fd = csiForFactor(d);
                trace.Add(new InverseStep(d, fd, false));
            }
        }
        double x = fc > fd ? c : d;
        double f = Math.Max(fc, fd);
        return new InverseResult(true, x, f, trace, $"Observed extent best matched at {x:P0} of the modelled release (CSI {f:F2}).");
    }
}
