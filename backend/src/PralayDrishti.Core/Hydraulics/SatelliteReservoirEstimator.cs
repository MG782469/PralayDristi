using PralayDrishti.Core.Geo;

namespace PralayDrishti.Core.Hydraulics;

/// <summary>
/// Reservoir volume with no ground data (transboundary case - Parechu on the Sutlej, Yarlung Tsangpo projects).
///   1. water spread area from Sentinel-2 NDWI / Sentinel-1 (done in GEE or from uploaded rasters)
///   2. AEV curve from the DEM around the lake
///   3. level from area (or directly from SWOT / ICESat-2 altimetry when available)
///   4. volume from level
/// Caveat: SRTM/GLO-30 already see the water surface of old reservoirs, so we only get storage above
/// the DEM water surface. Caller can pass an assumed dead storage to add.
/// </summary>
public static class SatelliteReservoirEstimator
{
    public sealed record Request(
        GeoPoint LakePoint,
        double? ObservedAreaKm2 = null,
        double? AltimetryLevel = null,
        double MaxRise = 300,
        double DeadStorageM3 = 0);

    public sealed record Result(
        double EstimatedLevel,
        double EstimatedVolumeM3,
        double AreaKm2,
        string LevelSource,
        StorageCurve Curve,
        List<int> LakeCells,
        List<string> Notes);

    public static Result Estimate(DemGrid dem, Request req)
    {
        if (req.ObservedAreaKm2 is null && req.AltimetryLevel is null)
            throw new ArgumentException("need either an observed water area or an altimetry level");

        if (!dem.TryCell(req.LakePoint, out int r, out int c))
            throw new ArgumentException("lake point outside DEM");

        // seed on the lowest cell in a small neighbourhood (the point may be clicked on the shore)
        int seed = dem.Idx(r, c);
        for (int rr = r - 5; rr <= r + 5; rr++)
            for (int cc = c - 5; cc <= c + 5; cc++)
                if (dem.InBounds(rr, cc) && dem[rr, cc] < dem.Z[seed]) seed = dem.Idx(rr, cc);

        double top = dem.Z[seed] + req.MaxRise;
        if (req.AltimetryLevel is double alt) top = Math.Max(top, alt + 5);
        var (curve, _) = StorageCurve.FromDem(dem, seed, top, step: 2);
        var notes = new List<string>();

        double level;
        string source;
        if (req.AltimetryLevel is double lvl)
        {
            level = lvl;
            source = "altimetry (SWOT / ICESat-2)";
        }
        else
        {
            double target = req.ObservedAreaKm2!.Value * 1e6;
            if (target > curve.Points[^1].AreaM2)
                notes.Add("Observed area exceeds what the DEM window can hold - enlarge the window.");
            level = curve.LevelAtArea(target);
            source = "water-spread area (NDWI) inverted through DEM AEV curve";
        }

        var cells = RasterOps.FillBelow(dem, dem.Z, seed, level);
        double vol = curve.VolumeAt(level) + req.DeadStorageM3;
        notes.Add("Volume is storage above the DEM water surface (SRTM/GLO-30 acquisition). Add dead storage if known.");
        if (req.ObservedAreaKm2 is double obs)
        {
            double model = cells.Count * dem.CellArea / 1e6;
            notes.Add($"Area check: observed {obs:F2} km2, DEM lake at estimated level {model:F2} km2.");
        }
        return new Result(level, vol, cells.Count * dem.CellArea / 1e6, source, curve, cells, notes);
    }

    /// <summary>NDWI = (Green - NIR) / (Green + NIR). Water where NDWI > threshold (0.0 - 0.2 typical).</summary>
    public static bool[] NdwiWaterMask(DemGrid green, DemGrid nir, double threshold = 0.1)
    {
        if (green.Count != nir.Count) throw new ArgumentException("band grids must have identical shape");
        var mask = new bool[green.Count];
        for (int i = 0; i < mask.Length; i++)
        {
            double g = green.Z[i], n = nir.Z[i];
            double s = g + n;
            mask[i] = s > 1e-6 && (g - n) / s > threshold;
        }
        return mask;
    }

    /// <summary>Area of the water body that contains the given point, km2.</summary>
    public static double WaterBodyAreaKm2(DemGrid grid, bool[] water, GeoPoint p)
    {
        if (!grid.TryCell(p, out int r, out int c)) return 0;
        var (labels, _) = RasterOps.Label(grid, water);
        int lab = labels[grid.Idx(r, c)];
        if (lab == 0) return 0;
        return labels.Count(l => l == lab) * grid.CellArea / 1e6;
    }
}
