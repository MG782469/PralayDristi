namespace PralayDrishti.Infrastructure.Engine.Geo;

/// <summary>
/// Generates an idealised mountain valley that opens into a floodplain. Used by the tests and as a
/// last-resort fallback when no DEM is available (air-gapped box, no cached tiles).
/// </summary>
public static class SyntheticTerrain
{
    public sealed record Valley(DemGrid Dem, GeoPoint DamSite, List<GeoPoint> RiverLine);

    public static Valley Build(GeoPoint origin, int cols = 360, int rows = 200, double cellMeters = 30,
        double bedSlope = 0.006, double upstreamElevation = 900)
    {
        double cellDeg = cellMeters / 110574.0;
        double west = origin.Lon - cols / 2.0 * cellDeg;
        double north = origin.Lat + rows / 2.0 * cellDeg;
        var dem = new DemGrid(cols, rows, west, north, cellDeg);
        double dx = dem.Dx, dy = dem.Dy;

        var river = new List<GeoPoint>();
        double midRow = rows / 2.0;
        for (int c = 0; c < cols; c++)
        {
            double x = c * dx;
            // gentle meander so the flow is not perfectly axis aligned
            double yCenter = midRow * dy + 18 * dy * Math.Sin(x / 2200.0);
            double bed = upstreamElevation - bedSlope * x;
            double t = (double)c / cols;
            // gorge upstream, opens up after ~55% of the length
            double floorHalfWidth = t < 0.55 ? 45 : 45 + (t - 0.55) * 2600;
            double sideSlope = t < 0.55 ? 0.9 : 0.9 - (t - 0.55) * 1.5;
            sideSlope = Math.Max(sideSlope, 0.05);

            for (int r = 0; r < rows; r++)
            {
                double y = (rows - r - 0.5) * dy;
                double off = Math.Abs(y - yCenter);
                double z = bed;
                if (off > floorHalfWidth) z += (off - floorHalfWidth) * sideSlope;
                // channel incised 3 m in the middle
                if (off < 20) z -= 3 * (1 - off / 20);
                dem[r, c] = (float)z;
            }
            int rr = Math.Clamp((int)(rows - yCenter / dy), 0, rows - 1);
            river.Add(dem.CellCenter(rr, c));
        }

        return new Valley(dem, river[(int)(cols * 0.18)], river);
    }

    /// <summary>A flat-bottom box channel, 1D-like, used for analytic comparisons (Ritter / Stoker).</summary>
    public static DemGrid Flume(int length, int width, double cellMeters, double bed = 0)
    {
        double cellDeg = cellMeters / 110574.0;
        // put it on the equator so Dx == Dy (almost) and the analytic comparison is clean
        var g = new DemGrid(length, width, 0, width * cellDeg, cellDeg);
        Array.Fill(g.Z, (float)bed);
        return g;
    }
}
