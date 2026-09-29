namespace PralayDrishti.Core.Analysis;

/// <summary>
/// Combined flood hazard curves (Smith, Davey &amp; Cox 2014; adopted in Australian Rainfall &amp; Runoff and
/// used by many dam-break studies). Classifies on depth, velocity and depth x velocity instead of plain wet/dry.
/// </summary>
public static class Hazard
{
    public static readonly string[] Labels =
    {
        "Dry",
        "H1 - generally safe",
        "H2 - unsafe for small vehicles",
        "H3 - unsafe for vehicles, children and elderly",
        "H4 - unsafe for people and all vehicles",
        "H5 - unsafe for all, buildings may be damaged",
        "H6 - all building types may fail"
    };

    public static readonly string[] ShortLabels = { "Dry", "H1", "H2", "H3", "H4", "H5", "H6" };

    public static int Classify(double depth, double velocity)
    {
        if (depth < 0.05) return 0;
        double dv = depth * velocity;
        if (dv <= 0.3 && depth <= 0.3 && velocity <= 2.0) return 1;
        if (dv <= 0.6 && depth <= 0.5 && velocity <= 2.0) return 2;
        if (dv <= 0.6 && depth <= 1.2 && velocity <= 2.0) return 3;
        if (dv <= 1.0 && depth <= 2.0 && velocity <= 2.0) return 4;
        if (dv <= 4.0 && depth <= 4.0 && velocity <= 4.0) return 5;
        return 6;
    }

    public static byte[] ClassifyGrid(float[] maxDepth, float[] maxSpeed)
    {
        var cls = new byte[maxDepth.Length];
        for (int i = 0; i < cls.Length; i++)
            cls[i] = (byte)Classify(maxDepth[i], maxSpeed[i]);
        return cls;
    }

    /// <summary>What it means for people on the ground - used in the sitrep.</summary>
    public static string Advice(int cls) => cls switch
    {
        0 => "No flooding expected.",
        1 => "Shallow water. Avoid walking in flow.",
        2 => "Keep small vehicles out. Move to upper floors if water rises.",
        3 => "Evacuate children, elderly and vehicles before arrival.",
        4 => "Full evacuation before arrival - people and vehicles will be swept away.",
        5 => "Full evacuation. Structural damage to buildings likely.",
        _ => "Full evacuation. Buildings of all types may collapse."
    };
}
