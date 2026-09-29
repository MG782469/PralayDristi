namespace PralayDrishti.Core.Geo;

public static class RasterOps
{
    public static bool PointInPolygon(IReadOnlyList<GeoPoint> ring, GeoPoint p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Lat > p.Lat) != (b.Lat > p.Lat) &&
                p.Lon < (b.Lon - a.Lon) * (p.Lat - a.Lat) / (b.Lat - a.Lat) + a.Lon)
                inside = !inside;
        }
        return inside;
    }

    public static bool[] PolygonMask(DemGrid g, IReadOnlyList<GeoPoint> ring)
    {
        var mask = new bool[g.Count];
        var bb = BBox.FromPoints(ring);
        for (int r = 0; r < g.Rows; r++)
            for (int c = 0; c < g.Cols; c++)
            {
                var p = g.CellCenter(r, c);
                if (!bb.Contains(p)) continue;
                mask[g.Idx(r, c)] = PointInPolygon(ring, p);
            }
        // tiny polygons (thin debris dams) may not contain any cell centre: take the cells under the vertices
        if (!mask.Any(m => m))
            foreach (var v in ring)
                if (g.TryCell(v, out int rr, out int cc)) mask[g.Idx(rr, cc)] = true;
        return mask;
    }

    /// <summary>Bresenham, thickened so the line is 4-connected (water cannot leak diagonally through it).</summary>
    public static List<int> LineCells(DemGrid g, int r0, int c0, int r1, int c1)
    {
        var cells = new List<int>();
        int dr = Math.Abs(r1 - r0), dc = Math.Abs(c1 - c0);
        int sr = r0 < r1 ? 1 : -1, sc = c0 < c1 ? 1 : -1;
        int err = dc - dr;
        int r = r0, c = c0;
        while (true)
        {
            if (g.InBounds(r, c)) cells.Add(g.Idx(r, c));
            if (r == r1 && c == c1) break;
            int e2 = 2 * err;
            bool stepC = e2 > -dr, stepR = e2 < dc;
            if (stepC && stepR && g.InBounds(r, c + sc)) cells.Add(g.Idx(r, c + sc));
            if (stepC) { err -= dr; c += sc; }
            if (stepR) { err += dc; r += sr; }
        }
        return cells;
    }

    /// <summary>4-connected component labelling. Returns labels (0 = background) and component count.</summary>
    public static (int[] labels, int count) Label(DemGrid g, bool[] mask)
    {
        var labels = new int[g.Count];
        int next = 0;
        var stack = new Stack<int>();
        for (int i = 0; i < g.Count; i++)
        {
            if (!mask[i] || labels[i] != 0) continue;
            next++;
            labels[i] = next;
            stack.Push(i);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                foreach (var (nr, nc) in g.Neighbours4(k / g.Cols, k % g.Cols))
                {
                    int j = g.Idx(nr, nc);
                    if (mask[j] && labels[j] == 0) { labels[j] = next; stack.Push(j); }
                }
            }
        }
        return (labels, next);
    }

    /// <summary>Flood fill of cells with z below level, starting from seed (4-connected).</summary>
    public static List<int> FillBelow(DemGrid g, float[] z, int seed, double level, bool[]? blocked = null, int maxCells = int.MaxValue)
    {
        var result = new List<int>();
        if (z[seed] >= level) return result;
        var seen = new bool[g.Count];
        var q = new Queue<int>();
        q.Enqueue(seed);
        seen[seed] = true;
        while (q.Count > 0 && result.Count < maxCells)
        {
            int i = q.Dequeue();
            result.Add(i);
            foreach (var (nr, nc) in g.Neighbours4(i / g.Cols, i % g.Cols))
            {
                int j = g.Idx(nr, nc);
                if (seen[j] || z[j] >= level || (blocked != null && blocked[j])) continue;
                seen[j] = true;
                q.Enqueue(j);
            }
        }
        return result;
    }

    public static double RingAreaM2(IReadOnlyList<GeoPoint> ring)
    {
        if (ring.Count < 3) return 0;
        double lat0 = ring.Average(p => p.Lat) * Math.PI / 180;
        double kx = 111320 * Math.Cos(lat0), ky = 110574;
        double s = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            s += (ring[j].Lon * kx) * (ring[i].Lat * ky) - (ring[i].Lon * kx) * (ring[j].Lat * ky);
        return Math.Abs(s) / 2;
    }

    public static double DistanceToPolyline(GeoPoint p, IReadOnlyList<GeoPoint> line)
    {
        double best = double.MaxValue;
        double kx = 111320 * Math.Cos(p.Lat * Math.PI / 180), ky = 110574;
        for (int i = 1; i < line.Count; i++)
        {
            double ax = (line[i - 1].Lon - p.Lon) * kx, ay = (line[i - 1].Lat - p.Lat) * ky;
            double bx = (line[i].Lon - p.Lon) * kx, by = (line[i].Lat - p.Lat) * ky;
            double vx = bx - ax, vy = by - ay;
            double len2 = vx * vx + vy * vy;
            double t = len2 < 1e-9 ? 0 : Math.Clamp(-(ax * vx + ay * vy) / len2, 0, 1);
            double x = ax + t * vx, y = ay + t * vy;
            best = Math.Min(best, Math.Sqrt(x * x + y * y));
        }
        return best;
    }
}
