namespace PralayDrishti.Infrastructure.Engine.Geo;

public readonly record struct GeoPoint(double Lon, double Lat)
{
    private const double EarthRadius = 6371008.8;

    public double DistanceTo(GeoPoint other)
    {
        double dLat = ToRad(other.Lat - Lat);
        double dLon = ToRad(other.Lon - Lon);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRad(Lat)) * Math.Cos(ToRad(other.Lat)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadius * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public GeoPoint Offset(double eastMeters, double northMeters)
    {
        double dLat = northMeters / 110574.0;
        double dLon = eastMeters / (111320.0 * Math.Cos(ToRad(Lat)));
        return new GeoPoint(Lon + dLon, Lat + dLat);
    }

    public override string ToString() => $"{Lat:F5}N {Lon:F5}E";

    private static double ToRad(double d) => d * Math.PI / 180.0;
}

public readonly record struct BBox(double West, double South, double East, double North)
{
    public double Width => East - West;
    public double Height => North - South;
    public GeoPoint Center => new((West + East) / 2, (South + North) / 2);

    public bool Contains(GeoPoint p) => p.Lon >= West && p.Lon <= East && p.Lat >= South && p.Lat <= North;

    public bool Intersects(BBox o) => !(o.West > East || o.East < West || o.South > North || o.North < South);

    public BBox Expand(double deg) => new(West - deg, South - deg, East + deg, North + deg);

    public static BBox Around(GeoPoint c, double halfWidthMeters, double halfHeightMeters)
    {
        var ne = c.Offset(halfWidthMeters, halfHeightMeters);
        var sw = c.Offset(-halfWidthMeters, -halfHeightMeters);
        return new BBox(sw.Lon, sw.Lat, ne.Lon, ne.Lat);
    }

    public static BBox FromPoints(IEnumerable<GeoPoint> pts)
    {
        double w = double.MaxValue, s = double.MaxValue, e = double.MinValue, n = double.MinValue;
        foreach (var p in pts)
        {
            w = Math.Min(w, p.Lon); e = Math.Max(e, p.Lon);
            s = Math.Min(s, p.Lat); n = Math.Max(n, p.Lat);
        }
        return new BBox(w, s, e, n);
    }
}
