namespace PralayDrishti.Core.Geo;

/// <summary>
/// DEM conditioning: depression filling, D8 flow routing, accumulation and river tracing.
/// Same ideas as pysheds / WhiteboxTools, rewritten so the backend has no Python dependency.
/// </summary>
public static class Hydrology
{
    /// <summary>
    /// Priority-flood (Barnes et al. 2014) with a tiny epsilon gradient so flats still drain.
    /// Returns a new elevation array; the input is left alone.
    /// </summary>
    public static float[] FillDepressions(DemGrid dem, float epsilon = 1e-3f)
    {
        var z = (float[])dem.Z.Clone();
        var done = new bool[dem.Count];
        var pq = new PriorityQueue<int, float>();

        for (int r = 0; r < dem.Rows; r++)
            for (int c = 0; c < dem.Cols; c++)
                if (r == 0 || c == 0 || r == dem.Rows - 1 || c == dem.Cols - 1)
                {
                    int i = dem.Idx(r, c);
                    pq.Enqueue(i, z[i]);
                    done[i] = true;
                }

        while (pq.TryDequeue(out int i, out float zi))
        {
            int r = i / dem.Cols, c = i % dem.Cols;
            foreach (var (dr, dc) in DemGrid.D8)
            {
                int nr = r + dr, nc = c + dc;
                if (!dem.InBounds(nr, nc)) continue;
                int j = dem.Idx(nr, nc);
                if (done[j]) continue;
                done[j] = true;
                if (z[j] <= zi) z[j] = zi + epsilon;
                pq.Enqueue(j, z[j]);
            }
        }
        return z;
    }

    /// <summary>D8 receiver index for every cell, -1 for outlets.</summary>
    public static int[] FlowDirections(DemGrid dem, float[] filled)
    {
        var rec = new int[dem.Count];
        double dx = dem.Dx, dy = dem.Dy, dd = Math.Sqrt(dx * dx + dy * dy);
        Parallel.For(0, dem.Rows, r =>
        {
            for (int c = 0; c < dem.Cols; c++)
            {
                int i = dem.Idx(r, c);
                int best = -1;
                double bestSlope = 0;
                foreach (var (dr, dc) in DemGrid.D8)
                {
                    int nr = r + dr, nc = c + dc;
                    if (!dem.InBounds(nr, nc)) continue;
                    double dist = dr != 0 && dc != 0 ? dd : (dr != 0 ? dy : dx);
                    double s = (filled[i] - filled[dem.Idx(nr, nc)]) / dist;
                    if (s > bestSlope) { bestSlope = s; best = dem.Idx(nr, nc); }
                }
                rec[i] = best;
            }
        });
        return rec;
    }

    /// <summary>Upstream contributing cell count.</summary>
    public static int[] Accumulation(DemGrid dem, float[] filled, int[] receivers)
    {
        var order = Enumerable.Range(0, dem.Count).ToArray();
        // high to low: every cell is processed before its receiver
        Array.Sort(order, (a, b) => filled[b].CompareTo(filled[a]));
        var acc = new int[dem.Count];
        Array.Fill(acc, 1);
        foreach (int i in order)
        {
            int j = receivers[i];
            if (j >= 0) acc[j] += acc[i];
        }
        return acc;
    }

    public sealed record Routing(float[] Filled, int[] Receivers, int[] Accumulation);

    public static Routing Route(DemGrid dem)
    {
        var filled = FillDepressions(dem);
        var rec = FlowDirections(dem, filled);
        return new Routing(filled, rec, Accumulation(dem, filled, rec));
    }

    /// <summary>
    /// Moves a user supplied point (dam location from a register, usually off by a few hundred metres)
    /// onto the cell with the largest accumulation within the search radius.
    /// </summary>
    public static int SnapToRiver(DemGrid dem, int[] acc, GeoPoint p, double radiusMeters = 600)
    {
        if (!dem.TryCell(p, out int r0, out int c0))
            throw new ArgumentException($"point {p} lies outside the DEM");
        int rad = Math.Max(1, (int)(radiusMeters / Math.Min(dem.Dx, dem.Dy)));
        int best = dem.Idx(r0, c0);
        for (int r = r0 - rad; r <= r0 + rad; r++)
            for (int c = c0 - rad; c <= c0 + rad; c++)
            {
                if (!dem.InBounds(r, c)) continue;
                int i = dem.Idx(r, c);
                if (acc[i] > acc[best]) best = i;
            }
        return best;
    }

    /// <summary>Follows D8 receivers from a start cell until the grid edge (or maxLength metres).</summary>
    public static List<int> TraceDownstream(DemGrid dem, int[] receivers, int start, double maxLength = double.MaxValue)
    {
        var path = new List<int> { start };
        var seen = new HashSet<int> { start };
        double len = 0;
        int cur = start;
        while (receivers[cur] >= 0 && len < maxLength)
        {
            int nxt = receivers[cur];
            if (!seen.Add(nxt)) break;
            len += dem.CellCenter(cur).DistanceTo(dem.CellCenter(nxt));
            path.Add(nxt);
            cur = nxt;
        }
        return path;
    }

    /// <summary>Walks upstream along the largest contributing neighbour.</summary>
    public static List<int> TraceUpstream(DemGrid dem, int[] receivers, int[] acc, int start, double maxLength)
    {
        var path = new List<int> { start };
        double len = 0;
        int cur = start;
        while (len < maxLength)
        {
            int r = cur / dem.Cols, c = cur % dem.Cols, best = -1;
            foreach (var (dr, dc) in DemGrid.D8)
            {
                int nr = r + dr, nc = c + dc;
                if (!dem.InBounds(nr, nc)) continue;
                int j = dem.Idx(nr, nc);
                if (receivers[j] == cur && (best < 0 || acc[j] > acc[best])) best = j;
            }
            if (best < 0) break;
            len += dem.CellCenter(cur).DistanceTo(dem.CellCenter(best));
            path.Add(best);
            cur = best;
        }
        path.Reverse();
        return path;
    }

    /// <summary>
    /// No bathymetry in open DEMs: the water surface at acquisition time is what SRTM saw.
    /// We "burn" a nominal channel along the traced river so the main channel conveys low flows.
    /// This assumption goes in the limitations slide.
    /// </summary>
    public static void BurnChannel(DemGrid dem, IEnumerable<int> riverCells, float depth)
    {
        foreach (int i in riverCells) dem.Z[i] -= depth;
    }

    /// <summary>
    /// D8 paths step diagonally, but a finite-volume solver only exchanges water across cell faces. Insert the
    /// lower of the two corner cells at every diagonal step so the path is 4-connected.
    /// </summary>
    public static List<int> FourConnected(DemGrid dem, IReadOnlyList<int> path)
    {
        var res = new List<int>(path.Count * 2);
        for (int k = 0; k < path.Count; k++)
        {
            if (k > 0)
            {
                int a = path[k - 1], b = path[k];
                int ra = a / dem.Cols, ca = a % dem.Cols, rb = b / dem.Cols, cb = b % dem.Cols;
                if (ra != rb && ca != cb)
                {
                    int c1 = dem.Idx(ra, cb), c2 = dem.Idx(rb, ca);
                    res.Add(dem.Z[c1] <= dem.Z[c2] ? c1 : c2);
                }
            }
            res.Add(path[k]);
        }
        return res;
    }

    /// <summary>
    /// Makes the bed along a traced river non-increasing downstream. Coarse or void-filled DEMs (SRTM in a
    /// Himalayan gorge at 100 m cells) put spurious sills across the valley that trap the flood wave; the traced
    /// path already goes through them, so carving along it restores connectivity. Returns cells changed.
    /// </summary>
    public static int CarveMonotonic(DemGrid dem, IReadOnlyList<int> path, float minDrop = 0.01f)
    {
        if (path.Count < 2) return 0;
        int changed = 0;
        float prev = dem.Z[path[0]];
        for (int k = 1; k < path.Count; k++)
        {
            int i = path[k];
            float limit = prev - minDrop;
            if (dem.Z[i] > limit)
            {
                dem.Z[i] = limit;
                changed++;
            }
            prev = dem.Z[i];
        }
        return changed;
    }

    /// <summary>Along-path chainage (m) and bed elevation, used by the SPH section and plots.</summary>
    public static (double[] chainage, double[] bed) Profile(DemGrid dem, IReadOnlyList<int> path, float[]? z = null)
    {
        z ??= dem.Z;
        var ch = new double[path.Count];
        var bed = new double[path.Count];
        for (int k = 0; k < path.Count; k++)
        {
            if (k > 0) ch[k] = ch[k - 1] + dem.CellCenter(path[k - 1]).DistanceTo(dem.CellCenter(path[k]));
            bed[k] = z[path[k]];
        }
        return (ch, bed);
    }

    /// <summary>Unit vector (east, north) of the flow direction averaged over the first few path cells.</summary>
    public static (double ex, double ny) FlowVector(DemGrid dem, IReadOnlyList<int> path, int lookahead = 6)
    {
        int k = Math.Min(lookahead, path.Count - 1);
        if (k <= 0) return (1, 0);
        var a = dem.CellCenter(path[0]);
        var b = dem.CellCenter(path[k]);
        double ex = (b.Lon - a.Lon) * Math.Cos(a.Lat * Math.PI / 180);
        double ny = b.Lat - a.Lat;
        double n = Math.Sqrt(ex * ex + ny * ny);
        return n < 1e-12 ? (1, 0) : (ex / n, ny / n);
    }
}
