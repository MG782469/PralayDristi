using PralayDrishti.Core.Geo;

namespace PralayDrishti.Core.Hydraulics;

public sealed record StoragePoint(double Elevation, double AreaM2, double VolumeM3);

/// <summary>Area-elevation-volume (AEV) curve of a reservoir or a natural lake.</summary>
public sealed class StorageCurve
{
    public IReadOnlyList<StoragePoint> Points { get; }

    public StorageCurve(IReadOnlyList<StoragePoint> points)
    {
        if (points.Count < 2) throw new ArgumentException("storage curve needs at least two points");
        Points = points.OrderBy(p => p.Elevation).ToList();
    }

    public double MinElevation => Points[0].Elevation;
    public double MaxElevation => Points[^1].Elevation;
    public double MaxVolume => Points[^1].VolumeM3;

    public double VolumeAt(double level) => Interp(level, p => p.Elevation, p => p.VolumeM3);
    public double AreaAt(double level) => Interp(level, p => p.Elevation, p => p.AreaM2);
    public double LevelAtVolume(double v) => Interp(v, p => p.VolumeM3, p => p.Elevation);
    public double LevelAtArea(double a) => Interp(a, p => p.AreaM2, p => p.Elevation);

    private double Interp(double x, Func<StoragePoint, double> fx, Func<StoragePoint, double> fy)
    {
        if (x <= fx(Points[0])) return fy(Points[0]);
        for (int i = 1; i < Points.Count; i++)
        {
            double x0 = fx(Points[i - 1]), x1 = fx(Points[i]);
            if (x <= x1)
            {
                double t = x1 - x0 < 1e-12 ? 0 : (x - x0) / (x1 - x0);
                return fy(Points[i - 1]) + t * (fy(Points[i]) - fy(Points[i - 1]));
            }
        }
        // extrapolate linearly on the last segment - lets a reservoir be slightly over-full
        var a = Points[^2];
        var b = Points[^1];
        double dx = fx(b) - fx(a);
        return dx < 1e-12 ? fy(b) : fy(b) + (x - fx(b)) * (fy(b) - fy(a)) / dx;
    }

    /// <summary>
    /// When we only know the gross storage and dam height (NRLD register): V(h) = V0 (h/H)^m.
    /// m ~ 2.5 is typical for Himalayan gorge reservoirs, ~2 for wide valleys.
    /// </summary>
    public static StorageCurve PowerLaw(double invertElevation, double fullDepth, double fullVolume, double exponent = 2.5)
    {
        var pts = new List<StoragePoint>();
        for (int i = 0; i <= 40; i++)
        {
            double h = fullDepth * 1.1 * i / 40.0;
            double v = fullVolume * Math.Pow(h / fullDepth, exponent);
            double a = exponent * fullVolume / fullDepth * Math.Pow(h / fullDepth, exponent - 1);
            pts.Add(new StoragePoint(invertElevation + h, a, v));
        }
        return new StorageCurve(pts);
    }

    /// <summary>
    /// Builds the curve from the DEM by flood-filling from a seed cell (lowest point of the lake) at
    /// increasing levels. Blocked cells (the dam body itself) are excluded so water cannot "fill" the dam.
    /// </summary>
    public static (StorageCurve curve, List<int> cellsAtTop) FromDem(DemGrid dem, int seed, double topLevel,
        bool[]? blocked = null, double step = 1.0)
    {
        double bottom = dem.Z[seed];
        var pts = new List<StoragePoint> { new(bottom, 0, 0) };
        List<int> cells = new();
        double area = dem.CellArea;
        int n = Math.Max(4, (int)Math.Ceiling((topLevel - bottom) / step));
        for (int k = 1; k <= n; k++)
        {
            double level = bottom + (topLevel - bottom) * k / n;
            cells = RasterOps.FillBelow(dem, dem.Z, seed, level, blocked);
            double vol = 0;
            foreach (int i in cells) vol += (level - dem.Z[i]) * area;
            pts.Add(new StoragePoint(level, cells.Count * area, vol));
        }
        return (new StorageCurve(pts), cells);
    }
}
