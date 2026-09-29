namespace PralayDrishti.Core.Solvers;

/// <summary>
/// Exact 1D dam-break solutions on a horizontal frictionless bed, dam at x = 0.
/// Ritter (1892) for a dry downstream bed, Stoker (1957) for a wet one.
/// </summary>
public static class AnalyticDamBreak
{
    private const double G = 9.81;

    public static (double h, double u) Ritter(double h0, double x, double t)
    {
        if (t <= 0) return x < 0 ? (h0, 0) : (0, 0);
        double c0 = Math.Sqrt(G * h0);
        double xi = x / t;
        if (xi <= -c0) return (h0, 0);
        if (xi >= 2 * c0) return (0, 0);
        double h = Math.Pow(2 * c0 - xi, 2) / (9 * G);
        double u = 2.0 / 3.0 * (c0 + xi);
        return (h, u);
    }

    public static (double h, double u) Stoker(double h0, double h1, double x, double t)
    {
        if (h1 <= 1e-9) return Ritter(h0, x, t);
        if (t <= 0) return x < 0 ? (h0, 0) : (h1, 0);
        var (hm, um, s) = StokerState(h0, h1);
        double c0 = Math.Sqrt(G * h0), cm = Math.Sqrt(G * hm);
        double xi = x / t;
        if (xi <= -c0) return (h0, 0);
        if (xi <= um - cm) return (Math.Pow(2 * c0 - xi, 2) / (9 * G), 2.0 / 3.0 * (c0 + xi));
        if (xi <= s) return (hm, um);
        return (h1, 0);
    }

    /// <summary>Intermediate depth, velocity and bore speed for the wet-bed problem.</summary>
    public static (double hm, double um, double shock) StokerState(double h0, double h1)
    {
        double c0 = Math.Sqrt(G * h0), c1 = Math.Sqrt(G * h1);
        double Hm(double s) => 0.5 * h1 * (Math.Sqrt(1 + 8 * s * s / (G * h1)) - 1);
        double F(double s)
        {
            double hm = Hm(s);
            return s * (1 - h1 / hm) + 2 * Math.Sqrt(G * hm) - 2 * c0;
        }

        double lo = c1 * 1.0000001, hi = 2 * c0 + c1;
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (F(lo) * F(mid) <= 0) hi = mid; else lo = mid;
        }
        double sh = 0.5 * (lo + hi);
        double hmv = Hm(sh);
        return (hmv, sh * (1 - h1 / hmv), sh);
    }

    /// <summary>Peak unit discharge at the dam section for an instantaneous full collapse: 8/27 sqrt(g) h0^1.5.</summary>
    public static double RitterDamSectionUnitDischarge(double h0) => 8.0 / 27.0 * Math.Sqrt(G) * Math.Pow(h0, 1.5);
}
