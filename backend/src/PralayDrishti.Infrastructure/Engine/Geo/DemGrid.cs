namespace PralayDrishti.Infrastructure.Engine.Geo;

/// <summary>
/// Regular lat/lon raster (EPSG:4326). Row 0 is the northern edge.
/// Everything downstream (solver, analysis, exports) works on this one grid layout,
/// so the metric spacing is computed once from the centre latitude.
/// </summary>
public sealed class DemGrid
{
    public int Cols { get; }
    public int Rows { get; }
    public double West { get; }
    public double North { get; }
    public double CellSize { get; }        // degrees
    public float[] Z { get; }

    public DemGrid(int cols, int rows, double west, double north, double cellSize, float[]? z = null)
    {
        if (cols < 2 || rows < 2) throw new ArgumentException("grid too small");
        Cols = cols;
        Rows = rows;
        West = west;
        North = north;
        CellSize = cellSize;
        Z = z ?? new float[cols * rows];
        if (Z.Length != cols * rows) throw new ArgumentException("elevation array size mismatch");
    }

    public double East => West + Cols * CellSize;
    public double South => North - Rows * CellSize;
    public BBox Bounds => new(West, South, East, North);
    public int Count => Cols * Rows;

    public double CenterLat => (North + South) / 2;
    public double Dx => CellSize * 111320.0 * Math.Cos(CenterLat * Math.PI / 180);
    public double Dy => CellSize * 110574.0;
    public double CellArea => Dx * Dy;

    public float this[int r, int c]
    {
        get => Z[r * Cols + c];
        set => Z[r * Cols + c] = value;
    }

    public int Idx(int r, int c) => r * Cols + c;
    public bool InBounds(int r, int c) => r >= 0 && c >= 0 && r < Rows && c < Cols;

    public GeoPoint CellCenter(int r, int c) =>
        new(West + (c + 0.5) * CellSize, North - (r + 0.5) * CellSize);

    public GeoPoint CellCenter(int idx) => CellCenter(idx / Cols, idx % Cols);

    public bool TryCell(GeoPoint p, out int r, out int c)
    {
        c = (int)Math.Floor((p.Lon - West) / CellSize);
        r = (int)Math.Floor((North - p.Lat) / CellSize);
        return InBounds(r, c);
    }

    public int? CellIndex(GeoPoint p) => TryCell(p, out int r, out int c) ? Idx(r, c) : null;

    public double Sample(GeoPoint p)
    {
        double fx = (p.Lon - West) / CellSize - 0.5;
        double fy = (North - p.Lat) / CellSize - 0.5;
        int c0 = Math.Clamp((int)Math.Floor(fx), 0, Cols - 2);
        int r0 = Math.Clamp((int)Math.Floor(fy), 0, Rows - 2);
        double tx = Math.Clamp(fx - c0, 0, 1), ty = Math.Clamp(fy - r0, 0, 1);
        double a = this[r0, c0], b = this[r0, c0 + 1], c = this[r0 + 1, c0], d = this[r0 + 1, c0 + 1];
        return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty;
    }

    /// <summary>Slope in degrees using central differences.</summary>
    public float[] Slope()
    {
        var s = new float[Count];
        double dx = Dx, dy = Dy;
        Parallel.For(0, Rows, r =>
        {
            for (int c = 0; c < Cols; c++)
            {
                int cl = Math.Max(c - 1, 0), cr = Math.Min(c + 1, Cols - 1);
                int ru = Math.Max(r - 1, 0), rd = Math.Min(r + 1, Rows - 1);
                double gx = (this[r, cr] - this[r, cl]) / ((cr - cl) * dx);
                double gy = (this[rd, c] - this[ru, c]) / ((rd - ru) * dy);
                s[Idx(r, c)] = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180 / Math.PI);
            }
        });
        return s;
    }

    public DemGrid Crop(BBox box)
    {
        int c0 = Math.Max(0, (int)Math.Floor((box.West - West) / CellSize));
        int c1 = Math.Min(Cols, (int)Math.Ceiling((box.East - West) / CellSize));
        int r0 = Math.Max(0, (int)Math.Floor((North - box.North) / CellSize));
        int r1 = Math.Min(Rows, (int)Math.Ceiling((North - box.South) / CellSize));
        int nc = c1 - c0, nr = r1 - r0;
        if (nc < 2 || nr < 2) throw new ArgumentException("crop box does not overlap the DEM");
        var z = new float[nc * nr];
        for (int r = 0; r < nr; r++)
            Array.Copy(Z, (r + r0) * Cols + c0, z, r * nc, nc);
        return new DemGrid(nc, nr, West + c0 * CellSize, North - r0 * CellSize, CellSize, z);
    }

    /// <summary>Block-average down-sampling. factor=2 halves resolution.</summary>
    public DemGrid Coarsen(int factor)
    {
        if (factor <= 1) return Clone();
        int nc = Cols / factor, nr = Rows / factor;
        var z = new float[nc * nr];
        for (int r = 0; r < nr; r++)
            for (int c = 0; c < nc; c++)
            {
                double sum = 0;
                for (int i = 0; i < factor; i++)
                    for (int j = 0; j < factor; j++)
                        sum += this[r * factor + i, c * factor + j];
                z[r * nc + c] = (float)(sum / (factor * factor));
            }
        return new DemGrid(nc, nr, West, North, CellSize * factor, z);
    }

    /// <summary>Pick a coarsening factor so the grid stays under maxCells.</summary>
    public int SuggestFactor(int maxCells)
    {
        int f = 1;
        while ((Cols / f) * (Rows / f) > maxCells) f++;
        return f;
    }

    /// <summary>Maps an array from this grid onto a coarser/finer grid by nearest neighbour.</summary>
    public float[] ResampleTo(float[] values, DemGrid target)
    {
        var outp = new float[target.Count];
        for (int r = 0; r < target.Rows; r++)
            for (int c = 0; c < target.Cols; c++)
            {
                var p = target.CellCenter(r, c);
                outp[target.Idx(r, c)] = TryCell(p, out int rr, out int cc) ? values[Idx(rr, cc)] : 0f;
            }
        return outp;
    }

    public DemGrid Clone() => new(Cols, Rows, West, North, CellSize, (float[])Z.Clone());

    public DemGrid WithValues(float[] z) => new(Cols, Rows, West, North, CellSize, z);

    public void FillNoData(float noData)
    {
        // crude but good enough for SRTM voids: iterative neighbour averaging
        bool any = true;
        for (int pass = 0; pass < 200 && any; pass++)
        {
            any = false;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    float v = this[r, c];
                    if (!(float.IsNaN(v) || v == noData || v < -1000)) continue;
                    double sum = 0; int n = 0;
                    foreach (var (nr, nc) in Neighbours4(r, c))
                    {
                        float w = this[nr, nc];
                        if (float.IsNaN(w) || w == noData || w < -1000) continue;
                        sum += w; n++;
                    }
                    if (n > 0) this[r, c] = (float)(sum / n);
                    else any = true;
                }
        }
    }

    public IEnumerable<(int r, int c)> Neighbours4(int r, int c)
    {
        if (r > 0) yield return (r - 1, c);
        if (r < Rows - 1) yield return (r + 1, c);
        if (c > 0) yield return (r, c - 1);
        if (c < Cols - 1) yield return (r, c + 1);
    }

    public static readonly (int dr, int dc)[] D8 =
    {
        (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)
    };
}
