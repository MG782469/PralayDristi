namespace PralayDrishti.Infrastructure.Engine.Hydraulics;

/// <summary>
/// Parametric breach model in the spirit of Fread's DAMBRK / HEC-RAS "breach progression":
/// the breach grows linearly (or sine-curve) over tf, outflow is a broad-crested trapezoidal weir,
/// reservoir level is updated from the storage curve every step.
///   Q = 1.7 b H^1.5 + 1.35 z H^2.5      (SI)
/// </summary>
public static class BreachRouting
{
    public sealed record Options(
        double CrestElevation,
        double InvertElevation,
        double InitialLevel,
        double InflowM3s = 0,
        double Dt = 2.0,
        double MaxDuration = 12 * 3600,
        bool SineProgression = true,
        double StartDelay = 0);

    public static Hydrograph Route(StorageCurve storage, BreachGeometry breach, Options o)
    {
        var hyd = new Hydrograph { Source = breach.Method };
        double level = o.InitialLevel;
        double volume = storage.VolumeAt(level);
        double tf = Math.Max(breach.FormationTime, o.Dt);
        double finalBottom = Math.Max(o.CrestElevation - breach.Depth, o.InvertElevation);
        double peak = 0;
        double t = 0;
        int sample = Math.Max(1, (int)Math.Round(30 / o.Dt));   // store every ~30 s
        int step = 0;

        hyd.Add(0, 0, level);
        if (o.StartDelay > 0)
        {
            hyd.Add(o.StartDelay, 0, level);
            t = o.StartDelay;
        }

        while (t < o.MaxDuration)
        {
            double local = t - o.StartDelay;
            double frac = Math.Clamp(local / tf, 0, 1);
            if (o.SineProgression) frac = 0.5 * (1 - Math.Cos(Math.PI * frac));

            double zb = o.CrestElevation - (o.CrestElevation - finalBottom) * frac;
            double b = Math.Max(0.5, breach.BottomWidth * frac);
            double H = Math.Max(0, level - zb);
            double q = 1.7 * b * Math.Pow(H, 1.5) + 1.35 * breach.SideSlope * Math.Pow(H, 2.5);

            // do not take more than what is in the reservoir this step
            q = Math.Min(q, volume / o.Dt + o.InflowM3s);
            volume = Math.Max(0, volume + (o.InflowM3s - q) * o.Dt);
            level = storage.LevelAtVolume(volume);
            t += o.Dt;
            step++;
            peak = Math.Max(peak, q);

            if (step % sample == 0) hyd.Add(t, q, level);
            if (local > tf && q < 0.01 * peak && q < 50) break;
        }
        if (hyd.Time[^1] < t) hyd.Add(t, hyd.Discharge[^1], level);
        return hyd;
    }

    /// <summary>
    /// SPH handover: use the near-field SPH unit discharge (scaled to prototype and multiplied by breach width)
    /// for the first window, then continue with reservoir routing from whatever volume is left.
    /// </summary>
    public static Hydrograph Blend(Hydrograph nearField, Hydrograph routed, double handoverTime)
    {
        var h = new Hydrograph { Source = "SPH near-field + reservoir routing" };
        for (int i = 0; i < nearField.Time.Count && nearField.Time[i] <= handoverTime; i++)
            h.Add(nearField.Time[i], nearField.Discharge[i]);

        double released = h.Volume();
        // find the point in the routed curve where the same volume has left the reservoir
        double acc = 0, shift = routed.Time.Count > 0 ? routed.Time[^1] : 0;
        for (int i = 1; i < routed.Time.Count; i++)
        {
            acc += 0.5 * (routed.Discharge[i] + routed.Discharge[i - 1]) * (routed.Time[i] - routed.Time[i - 1]);
            if (acc >= released) { shift = routed.Time[i]; break; }
        }
        double tEnd = h.Time.Count > 0 ? h.Time[^1] : 0;
        for (int i = 0; i < routed.Time.Count; i++)
        {
            if (routed.Time[i] < shift) continue;
            double t = tEnd + (routed.Time[i] - shift);
            if (h.Time.Count > 0 && t <= h.Time[^1]) continue;
            h.Add(t, routed.Discharge[i], routed.Level[i]);
        }
        return h;
    }
}
