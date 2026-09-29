using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Infrastructure.Engine.Hydraulics;

/// <summary>Final breach geometry + formation time. Widths in metres, time in seconds.</summary>
public sealed record BreachGeometry(
    double AverageWidth,
    double BottomWidth,
    double Depth,
    double SideSlope,       // H:V
    double FormationTime,
    string Method,
    double? EmpiricalPeak = null);

public sealed record DamInput(
    DamType Type,
    double HeightM,           // breach height hb (crest to breach invert)
    double WaterDepthM,       // hw, water above breach invert at failure
    double VolumeM3,          // reservoir volume above breach invert
    FailureMode Mode = FailureMode.Overtopping,
    double CrestWidthM = 10,
    double CrestLengthM = 300);

public static class BreachModels
{
    private const double G = 9.81;

    public static BreachGeometry Compute(DamInput dam, BreachMethod method) => method switch
    {
        BreachMethod.Froehlich2008 => Froehlich2008(dam),
        BreachMethod.MacDonaldLangridgeMonopolis => MacDonaldLangridgeMonopolis(dam),
        BreachMethod.CostaSchuster => LandslideDam(dam),
        _ => Froehlich2008(dam)
    };

    /// <summary>Default choice per dam type - what we would pick without further info.</summary>
    public static BreachMethod DefaultFor(DamType t) => t switch
    {
        DamType.LandslideDam or DamType.MoraineDam => BreachMethod.CostaSchuster,
        _ => BreachMethod.Froehlich2008
    };

    /// <summary>
    /// Froehlich (2008), J. Hydraul. Eng. 134(12):
    ///   B_avg = 0.27 Ko Vw^0.32 hb^0.04       Ko = 1.3 overtopping, 1.0 otherwise
    ///   tf    = 63.2 sqrt(Vw / (g hb^2))      [s]
    ///   side slope 1.0 (overtopping) / 0.7 (piping)
    /// Concrete gravity dams do not erode like this; for them we fall back to a fast, near
    /// instantaneous partial failure (monolith wash-out) which is the usual CWC/DRIP assumption.
    /// </summary>
    public static BreachGeometry Froehlich2008(DamInput d)
    {
        if (d.Type == DamType.Concrete || d.Mode == FailureMode.Instantaneous)
        {
            double wConcrete = Math.Min(d.CrestLengthM * 0.5, Math.Max(3 * d.HeightM, 30));
            return new BreachGeometry(wConcrete, wConcrete, d.HeightM, 0, 360, "Concrete monolith failure (0.1 h)",
                FroehlichPeak(d));
        }
        double ko = d.Mode == FailureMode.Overtopping ? 1.3 : 1.0;
        double z = d.Mode == FailureMode.Overtopping ? 1.0 : 0.7;
        double bavg = 0.27 * ko * Math.Pow(d.VolumeM3, 0.32) * Math.Pow(d.HeightM, 0.04);
        bavg = Math.Min(bavg, d.CrestLengthM);
        double tf = 63.2 * Math.Sqrt(d.VolumeM3 / (G * d.HeightM * d.HeightM));
        double bottom = Math.Max(bavg - z * d.HeightM, 1);
        return new BreachGeometry(bavg, bottom, d.HeightM, z, tf, "Froehlich (2008)", FroehlichPeak(d));
    }

    /// <summary>Froehlich (1995) peak outflow: Qp = 0.607 Vw^0.295 hw^1.24.</summary>
    public static double FroehlichPeak(DamInput d) =>
        0.607 * Math.Pow(d.VolumeM3, 0.295) * Math.Pow(d.WaterDepthM, 1.24);

    /// <summary>
    /// MacDonald &amp; Langridge-Monopolis (1984), earthfill:
    ///   Er = 0.0261 (Vw hw)^0.769   eroded volume [m3]
    ///   tf = 0.0179 Er^0.364        [h]
    ///   Qp = 1.154 (Vw hw)^0.412
    /// Breach assumed trapezoidal with 0.5 H:V sides; width backed out of Er and the dam cross-section.
    /// </summary>
    public static BreachGeometry MacDonaldLangridgeMonopolis(DamInput d)
    {
        double vh = d.VolumeM3 * d.WaterDepthM;
        double er = 0.0261 * Math.Pow(vh, 0.769);
        double tf = 0.0179 * Math.Pow(er, 0.364) * 3600;
        double qp = 1.154 * Math.Pow(vh, 0.412);
        // cross-section area of the embankment through the breach, assuming 2.5H:1V faces both sides
        double damSection = d.HeightM * (d.CrestWidthM + 2.5 * d.HeightM);
        double bavg = Math.Clamp(er / damSection, 5, d.CrestLengthM);
        double z = 0.5;
        return new BreachGeometry(bavg, Math.Max(bavg - z * d.HeightM, 1), d.HeightM, z, tf,
            "MacDonald & Langridge-Monopolis (1984)", qp);
    }

    /// <summary>
    /// Landslide / moraine dams. Peak discharge from Costa &amp; Schuster (1988):
    ///   Qp = 0.0158 PE^0.41,  PE = hd * V * gamma_w   [J]
    /// Landslide dams are wide and breach mostly by overtopping incision, so width comes from Froehlich
    /// with the overtopping factor and a wider side slope; formation time is shortened because the
    /// debris is loose and non-cohesive (Rishi Ganga / Parechu behave like this).
    /// </summary>
    public static BreachGeometry LandslideDam(DamInput d)
    {
        double pe = d.HeightM * d.VolumeM3 * 9810.0;
        double qp = 0.0158 * Math.Pow(pe, 0.41);
        double bavg = 0.27 * 1.3 * Math.Pow(d.VolumeM3, 0.32) * Math.Pow(d.HeightM, 0.04);
        bavg = Math.Min(bavg * 1.2, d.CrestLengthM);
        double tf = 0.6 * 63.2 * Math.Sqrt(d.VolumeM3 / (G * d.HeightM * d.HeightM));
        double z = 1.5;
        return new BreachGeometry(bavg, Math.Max(bavg - z * d.HeightM, 1), d.HeightM, z, tf,
            "Costa & Schuster (1988) + Froehlich width", qp);
    }

    public static BreachGeometry Scale(BreachGeometry b, double widthFactor, double timeFactor) => b with
    {
        AverageWidth = b.AverageWidth * widthFactor,
        BottomWidth = Math.Max(1, b.BottomWidth * widthFactor),
        FormationTime = Math.Max(30, b.FormationTime * timeFactor)
    };
}
