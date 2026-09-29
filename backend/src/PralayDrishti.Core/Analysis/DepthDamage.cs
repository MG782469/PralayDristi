namespace PralayDrishti.Core.Analysis;

public enum LandUse { Residential, Commercial, Industrial, Agriculture, Infrastructure }

/// <summary>
/// Depth-damage functions, Asia curves from the JRC global flood depth-damage dataset
/// (Huizinga, de Moel &amp; Szewczyk 2017). Damage fraction vs water depth (m).
/// The unit values in INR are deliberately configurable - defaults are indicative only and should be
/// replaced with the state PWD schedule of rates before anyone quotes a number officially.
/// </summary>
public sealed class DepthDamage
{
    private static readonly double[] Depths = { 0, 0.5, 1, 1.5, 2, 3, 4, 5, 6 };

    private static readonly Dictionary<LandUse, double[]> Curves = new()
    {
        [LandUse.Residential] = new[] { 0, 0.33, 0.49, 0.62, 0.72, 0.87, 0.93, 0.98, 1.0 },
        [LandUse.Commercial] = new[] { 0, 0.38, 0.54, 0.66, 0.76, 0.88, 0.94, 0.98, 1.0 },
        [LandUse.Industrial] = new[] { 0, 0.28, 0.48, 0.63, 0.72, 0.86, 0.91, 0.96, 1.0 },
        [LandUse.Agriculture] = new[] { 0, 0.14, 0.37, 0.52, 0.56, 0.66, 0.83, 0.99, 1.0 },
        [LandUse.Infrastructure] = new[] { 0, 0.21, 0.37, 0.52, 0.64, 0.78, 0.91, 0.98, 1.0 },
    };

    public double ResidentialInrPerM2 { get; init; } = 12_000;
    public double CommercialInrPerM2 { get; init; } = 15_000;
    public double IndustrialInrPerM2 { get; init; } = 14_000;
    public double AgricultureInrPerHa { get; init; } = 60_000;
    public double RoadInrPerKm { get; init; } = 2_50_00_000;     // hill road reconstruction, per km
    public double BridgeInr { get; init; } = 15_00_00_000;

    public static double Fraction(LandUse use, double depth)
    {
        if (depth <= 0) return 0;
        var f = Curves[use];
        if (depth >= Depths[^1]) return f[^1];
        for (int i = 1; i < Depths.Length; i++)
            if (depth <= Depths[i])
            {
                double t = (depth - Depths[i - 1]) / (Depths[i] - Depths[i - 1]);
                return f[i - 1] + t * (f[i] - f[i - 1]);
            }
        return 1;
    }

    public double BuildingLoss(LandUse use, double depth, double floorAreaM2)
    {
        double unit = use switch
        {
            LandUse.Commercial => CommercialInrPerM2,
            LandUse.Industrial => IndustrialInrPerM2,
            _ => ResidentialInrPerM2
        };
        return Fraction(use, depth) * unit * floorAreaM2;
    }

    public double CropLoss(double depth, double areaHa) => Fraction(LandUse.Agriculture, depth) * AgricultureInrPerHa * areaHa;
}
