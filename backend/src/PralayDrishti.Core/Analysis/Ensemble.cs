namespace PralayDrishti.Core.Analysis;

public sealed record EnsembleMember(int Index, double WidthFactor, double TimeFactor, double ManningFactor, double VolumeFactor);

public sealed class EnsembleResult
{
    public required int Members { get; init; }
    public required float[] Probability { get; init; }   // P(depth > threshold)
    public required float[] P50Depth { get; init; }
    public required float[] P90Depth { get; init; }
    public required float[] MeanDepth { get; init; }
    public List<EnsembleMember> Samples { get; init; } = new();
    public List<double> FloodedAreaKm2 { get; init; } = new();
    public double Threshold { get; init; }
}

/// <summary>
/// Monte Carlo over the parameters nobody knows well: breach width, formation time, roughness and (for natural
/// lakes) the volume itself. Froehlich (2008) reports roughly +/-1/3 on width and a factor 2-3 on time, which
/// is where the spreads below come from.
/// </summary>
public static class Ensemble
{
    public static List<EnsembleMember> Sample(int n, int seed = 42, bool uncertainVolume = false)
    {
        var rnd = new Random(seed);
        var list = new List<EnsembleMember>();
        for (int i = 0; i < n; i++)
        {
            double w = Math.Clamp(Math.Exp(Normal(rnd) * 0.3), 0.5, 2.0);
            double t = Math.Clamp(Math.Exp(Normal(rnd) * 0.5), 0.3, 3.0);
            double m = 0.7 + 0.6 * rnd.NextDouble();
            double v = uncertainVolume ? Math.Clamp(Math.Exp(Normal(rnd) * 0.25), 0.5, 1.8) : 1.0;
            list.Add(new EnsembleMember(i, w, t, m, v));
        }
        // member 0 is always the deterministic "best estimate"
        if (n > 0) list[0] = new EnsembleMember(0, 1, 1, 1, 1);
        return list;
    }

    private static double Normal(Random r)
    {
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    public static EnsembleResult Aggregate(IReadOnlyList<float[]> depths, double cellAreaM2, double threshold = 0.3,
        List<EnsembleMember>? samples = null)
    {
        int n = depths[0].Length, m = depths.Count;
        var prob = new float[n];
        var p50 = new float[n];
        var p90 = new float[n];
        var mean = new float[n];
        Parallel.For(0, n, () => new float[m], (i, _, buf) =>
        {
            int wet = 0;
            double sum = 0;
            for (int k = 0; k < m; k++)
            {
                float d = depths[k][i];
                buf[k] = d;
                sum += d;
                if (d >= threshold) wet++;
            }
            prob[i] = (float)wet / m;
            mean[i] = (float)(sum / m);
            if (wet > 0)
            {
                Array.Sort(buf);
                p50[i] = buf[(int)(0.5 * (m - 1))];
                p90[i] = buf[(int)Math.Ceiling(0.9 * (m - 1))];
            }
            return buf;
        }, _ => { });

        return new EnsembleResult
        {
            Members = m,
            Probability = prob,
            P50Depth = p50,
            P90Depth = p90,
            MeanDepth = mean,
            Samples = samples ?? new(),
            FloodedAreaKm2 = depths.Select(d => d.Count(x => x >= threshold) * cellAreaM2 / 1e6).ToList(),
            Threshold = threshold
        };
    }
}
