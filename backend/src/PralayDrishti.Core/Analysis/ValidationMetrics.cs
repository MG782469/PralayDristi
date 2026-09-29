namespace PralayDrishti.Core.Analysis;

public sealed record Contingency(int Hits, int FalseAlarms, int Misses, int CorrectNegatives)
{
    public double Csi => Ratio(Hits, Hits + FalseAlarms + Misses);        // == IoU of the wet sets
    public double F1 => Ratio(2 * Hits, 2 * Hits + FalseAlarms + Misses);
    public double Pod => Ratio(Hits, Hits + Misses);
    public double Far => Ratio(FalseAlarms, Hits + FalseAlarms);
    public double Bias => Ratio(Hits + FalseAlarms, Hits + Misses);
    public double Accuracy => Ratio(Hits + CorrectNegatives, Hits + FalseAlarms + Misses + CorrectNegatives);

    private static double Ratio(double a, double b) => b <= 0 ? 0 : a / b;
}

public sealed record ArrivalObservation(string Name, int Cell, double ObservedMin);

public static class ValidationMetrics
{
    /// <summary>
    /// Cell-by-cell comparison of a simulated flood extent against an observed one (Sentinel-1 change detection).
    /// Permanent water (JRC GSW occurrence &gt; 80%) should be masked out, otherwise the river itself inflates CSI.
    /// </summary>
    public static Contingency Compare(bool[] simulated, bool[] observed, bool[]? ignore = null, bool[]? domain = null)
    {
        int a = 0, b = 0, c = 0, d = 0;
        for (int i = 0; i < simulated.Length; i++)
        {
            if (ignore != null && ignore[i]) continue;
            if (domain != null && !domain[i]) continue;
            bool s = simulated[i], o = observed[i];
            if (s && o) a++;
            else if (s) b++;
            else if (o) c++;
            else d++;
        }
        return new Contingency(a, b, c, d);
    }

    public static bool[] WetMask(float[] depth, double threshold = 0.1)
    {
        var m = new bool[depth.Length];
        for (int i = 0; i < m.Length; i++) m[i] = depth[i] >= threshold;
        return m;
    }

    /// <summary>RMSE and mean error (model - observed) of flood arrival times, minutes.</summary>
    public static (double rmse, double bias, int n) ArrivalError(float[] arrivalSeconds, IEnumerable<ArrivalObservation> obs)
    {
        double se = 0, e = 0;
        int n = 0;
        foreach (var o in obs)
        {
            float a = arrivalSeconds[o.Cell];
            if (float.IsNaN(a)) continue;
            double err = a / 60.0 - o.ObservedMin;
            se += err * err;
            e += err;
            n++;
        }
        return n == 0 ? (double.NaN, double.NaN, 0) : (Math.Sqrt(se / n), e / n, n);
    }

    /// <summary>L1/L2/Linf of a numeric profile against an analytic one.</summary>
    public static (double l1, double l2, double linf) Norms(IReadOnlyList<double> model, IReadOnlyList<double> exact)
    {
        double s1 = 0, s2 = 0, mx = 0, n1 = 0, n2 = 0;
        for (int i = 0; i < model.Count; i++)
        {
            double d = Math.Abs(model[i] - exact[i]);
            s1 += d; s2 += d * d; mx = Math.Max(mx, d);
            n1 += Math.Abs(exact[i]); n2 += exact[i] * exact[i];
        }
        return (n1 > 0 ? s1 / n1 : s1, n2 > 0 ? Math.Sqrt(s2 / n2) : Math.Sqrt(s2), mx);
    }
}
