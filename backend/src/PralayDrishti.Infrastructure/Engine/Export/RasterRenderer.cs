using PralayDrishti.Infrastructure.Engine.Geo;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PralayDrishti.Infrastructure.Engine.Export;

public enum LayerKind { Depth, Hazard, Arrival, Velocity, Probability, Snapshot }

public static class ColorMaps
{
    private static readonly (double v, Rgba32 c)[] DepthRamp =
    {
        (0.1, new Rgba32(198, 233, 255, 200)),
        (0.5, new Rgba32(130, 196, 245, 215)),
        (1.0, new Rgba32(66, 146, 222, 225)),
        (2.0, new Rgba32(33, 102, 191, 235)),
        (5.0, new Rgba32(24, 60, 150, 240)),
        (10.0, new Rgba32(40, 20, 110, 245)),
        (25.0, new Rgba32(20, 5, 60, 250)),
    };

    public static readonly Rgba32[] HazardColors =
    {
        new(0, 0, 0, 0),
        new(173, 216, 230, 200),
        new(255, 237, 160, 215),
        new(254, 196, 79, 225),
        new(253, 141, 60, 235),
        new(227, 26, 28, 240),
        new(128, 0, 38, 245),
    };

    // early arrival = hot colours
    private static readonly (double v, Rgba32 c)[] ArrivalRamp =
    {
        (0, new Rgba32(128, 0, 38, 235)),
        (15, new Rgba32(227, 26, 28, 230)),
        (30, new Rgba32(253, 141, 60, 225)),
        (60, new Rgba32(254, 217, 118, 215)),
        (120, new Rgba32(199, 233, 180, 205)),
        (240, new Rgba32(65, 182, 196, 200)),
        (480, new Rgba32(34, 94, 168, 200)),
    };

    private static readonly (double v, Rgba32 c)[] ProbRamp =
    {
        (0.05, new Rgba32(255, 255, 204, 150)),
        (0.25, new Rgba32(161, 218, 180, 190)),
        (0.5, new Rgba32(65, 182, 196, 215)),
        (0.75, new Rgba32(44, 127, 184, 230)),
        (0.9, new Rgba32(37, 52, 148, 245)),
    };

    private static readonly (double v, Rgba32 c)[] VelRamp =
    {
        (0.1, new Rgba32(255, 247, 236, 180)),
        (1, new Rgba32(253, 212, 158, 200)),
        (2, new Rgba32(252, 141, 89, 220)),
        (4, new Rgba32(215, 48, 31, 235)),
        (8, new Rgba32(127, 0, 0, 245)),
    };

    public static Rgba32 For(LayerKind kind, float v)
    {
        if (float.IsNaN(v)) return default;
        return kind switch
        {
            LayerKind.Depth or LayerKind.Snapshot => v < 0.1f ? default : Ramp(DepthRamp, v),
            LayerKind.Hazard => v < 1 ? default : HazardColors[Math.Clamp((int)v, 0, 6)],
            LayerKind.Arrival => Ramp(ArrivalRamp, v / 60.0),
            LayerKind.Velocity => v < 0.1f ? default : Ramp(VelRamp, v),
            LayerKind.Probability => v < 0.05f ? default : Ramp(ProbRamp, v),
            _ => default
        };
    }

    private static Rgba32 Ramp((double v, Rgba32 c)[] ramp, double x)
    {
        if (x <= ramp[0].v) return ramp[0].c;
        for (int i = 1; i < ramp.Length; i++)
            if (x <= ramp[i].v)
            {
                double t = (x - ramp[i - 1].v) / (ramp[i].v - ramp[i - 1].v);
                var a = ramp[i - 1].c;
                var b = ramp[i].c;
                return new Rgba32((byte)(a.R + t * (b.R - a.R)), (byte)(a.G + t * (b.G - a.G)),
                    (byte)(a.B + t * (b.B - a.B)), (byte)(a.A + t * (b.A - a.A)));
            }
        return ramp[^1].c;
    }

    public static IReadOnlyList<(string label, string hex)> Legend(LayerKind kind) => kind switch
    {
        LayerKind.Depth or LayerKind.Snapshot => DepthRamp.Select(r => ($"{r.v} m", Hex(r.c))).ToList(),
        LayerKind.Hazard => Enumerable.Range(1, 6).Select(i => ($"H{i}", Hex(HazardColors[i]))).ToList(),
        LayerKind.Arrival => ArrivalRamp.Select(r => ($"{r.v} min", Hex(r.c))).ToList(),
        LayerKind.Probability => ProbRamp.Select(r => ($"{r.v:P0}", Hex(r.c))).ToList(),
        LayerKind.Velocity => VelRamp.Select(r => ($"{r.v} m/s", Hex(r.c))).ToList(),
        _ => new List<(string, string)>()
    };

    private static string Hex(Rgba32 c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

public static class RasterRenderer
{
    /// <summary>One pixel per cell, transparent where there is nothing to show. Used for KMZ overlays.</summary>
    public static byte[] RenderGrid(DemGrid g, float[] values, LayerKind kind, bool[]? mask = null)
    {
        using var img = new Image<Rgba32>(g.Cols, g.Rows);
        img.ProcessPixelRows(acc =>
        {
            for (int r = 0; r < g.Rows; r++)
            {
                var row = acc.GetRowSpan(r);
                for (int c = 0; c < g.Cols; c++)
                {
                    int i = g.Idx(r, c);
                    row[c] = mask != null && mask[i] ? default : ColorMaps.For(kind, values[i]);
                }
            }
        });
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// WMS-style render: arbitrary bbox in EPSG:4326 or EPSG:3857, nearest-neighbour sampling of the result grid.
    /// </summary>
    public static byte[] RenderView(DemGrid g, float[] values, LayerKind kind, BBox bbox, bool mercator, int width, int height,
        bool[]? mask = null)
    {
        width = Math.Clamp(width, 16, 4096);
        height = Math.Clamp(height, 16, 4096);
        using var img = new Image<Rgba32>(width, height);
        img.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = acc.GetRowSpan(y);
                double fy = (y + 0.5) / height;
                double northing = bbox.North - fy * (bbox.North - bbox.South);
                double lat = mercator ? MercToLat(northing) : northing;
                for (int x = 0; x < width; x++)
                {
                    double fx = (x + 0.5) / width;
                    double easting = bbox.West + fx * (bbox.East - bbox.West);
                    double lon = mercator ? easting / 6378137.0 * 180 / Math.PI : easting;
                    if (!g.TryCell(new GeoPoint(lon, lat), out int r, out int c)) continue;
                    int i = g.Idx(r, c);
                    if (mask != null && mask[i]) continue;
                    row[x] = ColorMaps.For(kind, values[i]);
                }
            }
        });
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static double MercToLat(double y) => (2 * Math.Atan(Math.Exp(y / 6378137.0)) - Math.PI / 2) * 180 / Math.PI;

    /// <summary>Hillshade + flood overlay + markers, for the PDF sitrep.</summary>
    public static byte[] RenderMap(DemGrid g, float[] values, LayerKind kind, IEnumerable<(GeoPoint p, Rgba32 color)> markers,
        bool[]? mask = null, int maxSize = 1400)
    {
        int step = Math.Max(1, (int)Math.Ceiling(Math.Max(g.Cols, g.Rows) / (double)maxSize));
        int w = g.Cols / step, h = g.Rows / step;
        var shade = Hillshade(g);
        using var img = new Image<Rgba32>(w, h);
        img.ProcessPixelRows(acc =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = acc.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    int i = g.Idx(y * step, x * step);
                    byte s = (byte)(90 + shade[i] * 150);
                    var baseC = new Rgba32(s, s, (byte)Math.Min(255, s + 8), 255);
                    var top = mask != null && mask[i] ? default : ColorMaps.For(kind, values[i]);
                    double a = top.A / 255.0;
                    row[x] = new Rgba32((byte)(baseC.R * (1 - a) + top.R * a), (byte)(baseC.G * (1 - a) + top.G * a),
                        (byte)(baseC.B * (1 - a) + top.B * a), 255);
                }
            }
        });
        foreach (var (p, color) in markers)
        {
            if (!g.TryCell(p, out int r, out int c)) continue;
            int cx = c / step, cy = r / step;
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    bool edge = Math.Abs(dx) == 4 || Math.Abs(dy) == 4;
                    img[x, y] = edge ? new Rgba32(255, 255, 255, 255) : color;
                }
        }
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    public static float[] Hillshade(DemGrid g, double azimuth = 315, double altitude = 45)
    {
        var s = new float[g.Count];
        double az = (360 - azimuth + 90) * Math.PI / 180;
        double zen = (90 - altitude) * Math.PI / 180;
        double dx = g.Dx, dy = g.Dy;
        for (int r = 1; r < g.Rows - 1; r++)
            for (int c = 1; c < g.Cols - 1; c++)
            {
                double dzdx = ((g[r - 1, c + 1] + 2 * g[r, c + 1] + g[r + 1, c + 1]) - (g[r - 1, c - 1] + 2 * g[r, c - 1] + g[r + 1, c - 1])) / (8 * dx);
                double dzdy = ((g[r + 1, c - 1] + 2 * g[r + 1, c] + g[r + 1, c + 1]) - (g[r - 1, c - 1] + 2 * g[r - 1, c] + g[r - 1, c + 1])) / (8 * dy);
                double slope = Math.Atan(Math.Sqrt(dzdx * dzdx + dzdy * dzdy));
                double aspect = Math.Atan2(dzdy, -dzdx);
                double v = Math.Cos(zen) * Math.Cos(slope) + Math.Sin(zen) * Math.Sin(slope) * Math.Cos(az - aspect);
                s[g.Idx(r, c)] = (float)Math.Clamp(v, 0, 1);
            }
        return s;
    }
}
