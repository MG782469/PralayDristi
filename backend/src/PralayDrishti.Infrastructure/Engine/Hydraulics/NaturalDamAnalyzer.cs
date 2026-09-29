using PralayDrishti.Infrastructure.Engine.Geo;

namespace PralayDrishti.Infrastructure.Engine.Hydraulics;

public sealed record NaturalDamResult(
    double BedElevation,
    double CrestElevation,
    double DamHeight,
    double LakeAreaM2,
    double LakeVolumeM3,
    double MaxLakeDepth,
    double? CurrentLevel,
    double? CurrentVolumeM3,
    bool LakeTouchesDemEdge,
    bool SpillLoweredBySaddle,
    int SeedCell,
    int BlockageRiverCell,
    List<int> LakeCells,
    StorageCurve Curve,
    bool[] DebrisMask,
    List<string> Notes);

/// <summary>
/// "Natural Dam / Landslide Lake mode": the user draws the debris footprint on imagery, we work out
/// how high the lake can rise before it spills over the debris, the impounded volume and the AEV curve.
/// </summary>
public static class NaturalDamAnalyzer
{
    public sealed record Request(
        IReadOnlyList<GeoPoint> DebrisPolygon,
        double? CrestElevation = null,
        double? HeightAboveBed = null,
        double? CurrentLakeLevel = null);

    public static NaturalDamResult Analyze(DemGrid dem, Request req)
    {
        var notes = new List<string>();
        var mask = RasterOps.PolygonMask(dem, req.DebrisPolygon);
        var routing = Hydrology.Route(dem);

        // the river crosses the blockage where accumulation is largest inside the polygon
        int riverCell = -1;
        for (int i = 0; i < dem.Count; i++)
            if (mask[i] && (riverCell < 0 || routing.Accumulation[i] > routing.Accumulation[riverCell]))
                riverCell = i;
        if (riverCell < 0) throw new ArgumentException("debris polygon does not cover any DEM cell");

        var upstream = Hydrology.TraceUpstream(dem, routing.Receivers, routing.Accumulation, riverCell, 20_000);
        int seed = -1;
        for (int k = upstream.Count - 1; k >= 0; k--)
            if (!mask[upstream[k]]) { seed = upstream[k]; break; }
        if (seed < 0) throw new ArgumentException("could not find the river upstream of the blockage - draw the polygon across the valley");

        var downstream = Hydrology.TraceDownstream(dem, routing.Receivers, riverCell, 20_000);
        int below = downstream.FirstOrDefault(i => !mask[i], -1);

        double bed = dem.Z[riverCell];
        double crest;
        if (req.CrestElevation is double ce) crest = ce;
        else if (req.HeightAboveBed is double hb) crest = bed + hb;
        else
        {
            var inside = Enumerable.Range(0, dem.Count).Where(i => mask[i]).Select(i => (double)dem.Z[i]).OrderBy(z => z).ToList();
            double median = inside[inside.Count / 2];
            crest = bed + Math.Max(5, median - bed);
            notes.Add($"No dam height given - assumed debris crest at median polygon elevation ({crest - bed:F0} m above bed). Give height from imagery/field report for a better estimate.");
        }

        // raise the debris footprint to crest level - pre-event DEMs (SRTM 2000, GLO-30) don't have the debris
        var dammed = dem.Clone();
        for (int i = 0; i < dem.Count; i++)
            if (mask[i] && dammed.Z[i] < crest) dammed.Z[i] = (float)crest;

        // the lake can also spill through a side saddle before it reaches the crest
        bool lowered = false;
        double spill = crest;
        if (below >= 0 && ReachesCell(dammed, seed, spill, mask, below))
        {
            double lo = dammed.Z[seed], hi = crest;
            for (int it = 0; it < 30; it++)
            {
                double mid = 0.5 * (lo + hi);
                if (ReachesCell(dammed, seed, mid, mask, below)) hi = mid; else lo = mid;
            }
            spill = lo;
            lowered = true;
            notes.Add($"Lake would outflank the debris through a lower saddle; effective spill level {spill:F1} m.");
        }

        var (curve, cells) = StorageCurve.FromDem(dammed, seed, spill, mask, step: Math.Max(0.5, (spill - dammed.Z[seed]) / 60));
        bool edge = cells.Any(i => { int r = i / dem.Cols, c = i % dem.Cols; return r == 0 || c == 0 || r == dem.Rows - 1 || c == dem.Cols - 1; });
        if (edge) notes.Add("Lake reaches the DEM boundary - extend the area of interest upstream, volume is a lower bound.");

        double? curVol = null;
        if (req.CurrentLakeLevel is double cl)
            curVol = curve.VolumeAt(Math.Min(cl, spill));

        double maxDepth = cells.Count == 0 ? 0 : cells.Max(i => spill - dammed.Z[i]);
        return new NaturalDamResult(bed, spill, spill - bed, curve.Points[^1].AreaM2, curve.MaxVolume, maxDepth,
            req.CurrentLakeLevel, curVol, edge, lowered, seed, riverCell, cells, curve, mask, notes);
    }

    private static bool ReachesCell(DemGrid g, int seed, double level, bool[] blocked, int target)
    {
        var cells = RasterOps.FillBelow(g, g.Z, seed, level, blocked);
        return cells.Contains(target);
    }
}
