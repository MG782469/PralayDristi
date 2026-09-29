using NetTopologySuite;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;
using PralayDrishti.Infrastructure.Engine.Geo;

namespace PralayDrishti.Infrastructure.Engine.Export;

public sealed record ClassBand(string Label, double Min, double Max);

/// <summary>Raster classes -> polygons. Row runs become rectangles, then unary union + light simplification.</summary>
public static class Vectorizer
{
    public static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    public static readonly ClassBand[] DepthBands =
    {
        new("0.1-0.5 m", 0.1, 0.5), new("0.5-1 m", 0.5, 1), new("1-2 m", 1, 2),
        new("2-5 m", 2, 5), new("5-10 m", 5, 10), new(">10 m", 10, double.MaxValue)
    };

    public static readonly ClassBand[] ArrivalBands =
    {
        new("0-15 min", 0, 15 * 60), new("15-30 min", 15 * 60, 30 * 60), new("30-60 min", 30 * 60, 3600),
        new("1-2 h", 3600, 7200), new("2-4 h", 7200, 14400), new(">4 h", 14400, double.MaxValue)
    };

    public static Geometry? Polygonize(DemGrid g, Func<int, bool> include, double simplifyCells = 0.5)
    {
        var rects = new List<Geometry>();
        for (int r = 0; r < g.Rows; r++)
        {
            int c = 0;
            while (c < g.Cols)
            {
                if (!include(g.Idx(r, c))) { c++; continue; }
                int start = c;
                while (c < g.Cols && include(g.Idx(r, c))) c++;
                double x0 = g.West + start * g.CellSize, x1 = g.West + c * g.CellSize;
                double y1 = g.North - r * g.CellSize, y0 = y1 - g.CellSize;
                rects.Add(Factory.CreatePolygon(new[]
                {
                    new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1),
                    new Coordinate(x0, y1), new Coordinate(x0, y0)
                }));
            }
        }
        if (rects.Count == 0) return null;
        var union = UnaryUnionOp.Union(rects);
        if (simplifyCells > 0)
            union = TopologyPreservingSimplifier.Simplify(union, g.CellSize * simplifyCells);
        return union;
    }

    public static List<IFeature> Bands(DemGrid g, float[] values, ClassBand[] bands, string attr, bool[]? exclude = null)
    {
        var list = new List<IFeature>();
        foreach (var b in bands)
        {
            var geom = Polygonize(g, i => (exclude == null || !exclude[i]) && !float.IsNaN(values[i]) &&
                                          values[i] >= b.Min && values[i] < b.Max);
            if (geom == null || geom.IsEmpty) continue;
            list.Add(new Feature(geom, new AttributesTable
            {
                { attr, b.Label }, { "min", b.Min }, { "max", b.Max >= double.MaxValue ? -1 : b.Max },
                { "area_km2", Math.Round(AreaKm2(geom, g), 4) }
            }));
        }
        return list;
    }

    public static List<IFeature> Classes(DemGrid g, byte[] cls, string[] labels, bool[]? exclude = null)
    {
        var list = new List<IFeature>();
        for (int k = 1; k < labels.Length; k++)
        {
            byte kk = (byte)k;
            var geom = Polygonize(g, i => cls[i] == kk && (exclude == null || !exclude[i]));
            if (geom == null || geom.IsEmpty) continue;
            list.Add(new Feature(geom, new AttributesTable
            {
                { "class", $"H{k}" }, { "label", labels[k] }, { "area_km2", Math.Round(AreaKm2(geom, g), 4) }
            }));
        }
        return list;
    }

    public static Geometry? Extent(DemGrid g, float[] depth, double threshold, bool[]? exclude = null) =>
        Polygonize(g, i => depth[i] >= threshold && (exclude == null || !exclude[i]), 1.0);

    public static double AreaKm2(Geometry geom, DemGrid g)
    {
        double kx = 111320 * Math.Cos(g.CenterLat * Math.PI / 180), ky = 110574;
        return geom.Area * kx * ky / 1e6;
    }

    public static Point PointOf(GeoPoint p) => Factory.CreatePoint(new Coordinate(p.Lon, p.Lat));

    public static LineString LineOf(IEnumerable<GeoPoint> pts) =>
        Factory.CreateLineString(pts.Select(p => new Coordinate(p.Lon, p.Lat)).ToArray());
}
