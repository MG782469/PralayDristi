using PralayDrishti.Infrastructure.Engine.Geo;

namespace PralayDrishti.Infrastructure.Engine.Analysis;

public sealed record NewWaterBody(GeoPoint Centroid, double AreaKm2, double MinElevation, int Cells, List<int> CellIndices);

/// <summary>
/// Offline twin of the GEE lake watch: given two water masks (before/after, e.g. NDWI &gt; 0.1) find water bodies
/// that did not exist before. A new lake in a Himalayan valley usually means a landslide has blocked the river.
/// </summary>
public static class LakeChangeDetector
{
    public static List<NewWaterBody> Detect(DemGrid grid, bool[] before, bool[] after, double minAreaKm2 = 0.02)
    {
        var fresh = new bool[grid.Count];
        for (int i = 0; i < fresh.Length; i++) fresh[i] = after[i] && !before[i];
        var (labels, count) = RasterOps.Label(grid, fresh);
        var buckets = new List<int>[count + 1];
        for (int i = 0; i < labels.Length; i++)
        {
            int l = labels[i];
            if (l == 0) continue;
            (buckets[l] ??= new List<int>()).Add(i);
        }

        var result = new List<NewWaterBody>();
        double cellKm2 = grid.CellArea / 1e6;
        for (int l = 1; l <= count; l++)
        {
            var cells = buckets[l];
            if (cells == null || cells.Count * cellKm2 < minAreaKm2) continue;
            double lon = cells.Average(i => grid.CellCenter(i).Lon);
            double lat = cells.Average(i => grid.CellCenter(i).Lat);
            result.Add(new NewWaterBody(new GeoPoint(lon, lat), cells.Count * cellKm2, cells.Min(i => grid.Z[i]), cells.Count, cells));
        }
        return result.OrderByDescending(r => r.AreaKm2).ToList();
    }
}
