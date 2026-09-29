using System.Text.Json.Serialization;

namespace PralayDrishti.Core.Hydraulics;

// Populate: the lists are get-only, so let System.Text.Json fill them when a run is loaded back from disk
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed class Hydrograph
{
    public List<double> Time { get; } = new();          // s
    public List<double> Discharge { get; } = new();     // m3/s
    public List<double> Level { get; } = new();         // reservoir level (m), NaN if not tracked
    public string Source { get; set; } = "";

    public void Add(double t, double q, double level = double.NaN)
    {
        Time.Add(t);
        Discharge.Add(q);
        Level.Add(level);
    }

    public double Peak => Discharge.Count == 0 ? 0 : Discharge.Max();
    public double TimeToPeak => Discharge.Count == 0 ? 0 : Time[Discharge.IndexOf(Peak)];
    public double Duration => Time.Count == 0 ? 0 : Time[^1];

    public double Volume()
    {
        double v = 0;
        for (int i = 1; i < Time.Count; i++)
            v += 0.5 * (Discharge[i] + Discharge[i - 1]) * (Time[i] - Time[i - 1]);
        return v;
    }

    public double At(double t)
    {
        if (Time.Count == 0) return 0;
        if (t <= Time[0]) return Discharge[0];
        if (t >= Time[^1]) return Discharge[^1];
        int lo = 0, hi = Time.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (Time[mid] <= t) lo = mid; else hi = mid;
        }
        double f = (t - Time[lo]) / (Time[hi] - Time[lo]);
        return Discharge[lo] + f * (Discharge[hi] - Discharge[lo]);
    }

    public Hydrograph Scaled(double factor)
    {
        var h = new Hydrograph { Source = $"{Source} x{factor:F2}" };
        for (int i = 0; i < Time.Count; i++) h.Add(Time[i], Discharge[i] * factor, Level[i]);
        return h;
    }

    /// <summary>Thin out to at most n samples, handy before sending to the dashboard.</summary>
    public Hydrograph Decimate(int n)
    {
        if (Time.Count <= n) return this;
        var h = new Hydrograph { Source = Source };
        double step = (Time.Count - 1) / (double)(n - 1);
        for (int k = 0; k < n; k++)
        {
            int i = (int)Math.Round(k * step);
            h.Add(Time[i], Discharge[i], Level[i]);
        }
        // never lose the peak when thinning
        int ip = Discharge.IndexOf(Peak);
        if (!h.Time.Contains(Time[ip]))
        {
            int at = h.Time.FindIndex(t => t > Time[ip]);
            if (at < 0) at = h.Time.Count;
            h.Time.Insert(at, Time[ip]);
            h.Discharge.Insert(at, Discharge[ip]);
            h.Level.Insert(at, Level[ip]);
        }
        return h;
    }
}
