using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PralayDrishti.Infrastructure.Engine.Geo;

/// <summary>
/// Pulls DEM data from the public AWS "Terrain Tiles" bucket (terrarium PNG encoding, built from SRTM/GMTED/etc.)
/// and caches every tile on disk. Once a valley has been fetched it works fully offline, which is how the
/// demo laptop runs without internet.
/// </summary>
public sealed class TerrainTileSource
{
    private const string UrlTemplate = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{0}/{1}/{2}.png";
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly bool _offline;

    public TerrainTileSource(HttpClient http, string cacheDir, bool offline = false)
    {
        _http = http;
        _cacheDir = cacheDir;
        _offline = offline;
        Directory.CreateDirectory(cacheDir);
    }

    public static double CellSizeForZoom(int zoom) => 360.0 / (256.0 * (1 << zoom));

    public async Task<DemGrid> FetchAsync(BBox box, int zoom = 12, CancellationToken ct = default)
    {
        double cell = CellSizeForZoom(zoom);
        int cols = Math.Max(2, (int)Math.Ceiling(box.Width / cell));
        int rows = Math.Max(2, (int)Math.Ceiling(box.Height / cell));
        if ((long)cols * rows > 12_000_000)
            throw new ArgumentException("area too large for this zoom level, use a lower zoom or upload a DEM");

        var (tx0, ty0) = TileOf(box.West, box.North, zoom);
        var (tx1, ty1) = TileOf(box.East, box.South, zoom);

        var tiles = new Dictionary<(int, int), Image<Rgb24>>();
        try
        {
            for (int tx = tx0; tx <= tx1; tx++)
                for (int ty = ty0; ty <= ty1; ty++)
                    tiles[(tx, ty)] = await LoadTileAsync(zoom, tx, ty, ct);

            var z = new float[cols * rows];
            double worldPx = 256.0 * (1 << zoom);
            for (int r = 0; r < rows; r++)
            {
                double lat = box.North - (r + 0.5) * cell;
                double py = MercY(lat) * worldPx;
                for (int c = 0; c < cols; c++)
                {
                    double lon = box.West + (c + 0.5) * cell;
                    double px = (lon + 180.0) / 360.0 * worldPx;
                    int tx = (int)(px / 256), ty = (int)(py / 256);
                    if (!tiles.TryGetValue((tx, ty), out var img)) continue;
                    int ix = Math.Clamp((int)(px - tx * 256), 0, 255);
                    int iy = Math.Clamp((int)(py - ty * 256), 0, 255);
                    var p = img[ix, iy];
                    z[r * cols + c] = (float)(p.R * 256.0 + p.G + p.B / 256.0 - 32768.0);
                }
            }
            return new DemGrid(cols, rows, box.West, box.North, cell, z);
        }
        finally
        {
            foreach (var img in tiles.Values) img.Dispose();
        }
    }

    private async Task<Image<Rgb24>> LoadTileAsync(int z, int x, int y, CancellationToken ct)
    {
        string file = Path.Combine(_cacheDir, z.ToString(), x.ToString(), $"{y}.png");
        if (!File.Exists(file))
        {
            if (_offline)
                throw new InvalidOperationException($"tile {z}/{x}/{y} not cached and offline mode is on");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var bytes = await _http.GetByteArrayAsync(string.Format(UrlTemplate, z, x, y), ct);
            await File.WriteAllBytesAsync(file, bytes, ct);
        }
        return await Image.LoadAsync<Rgb24>(file, ct);
    }

    private static (int x, int y) TileOf(double lon, double lat, int z)
    {
        int n = 1 << z;
        int x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        int y = (int)Math.Floor(MercY(lat) * n);
        return (Math.Clamp(x, 0, n - 1), Math.Clamp(y, 0, n - 1));
    }

    private static double MercY(double lat)
    {
        double rad = lat * Math.PI / 180;
        return (1 - Math.Log(Math.Tan(rad) + 1 / Math.Cos(rad)) / Math.PI) / 2;
    }
}
