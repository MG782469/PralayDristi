using System.Globalization;
using BitMiracle.LibTiff.Classic;

namespace PralayDrishti.Infrastructure.Engine.Geo;

/// <summary>
/// Minimal GeoTIFF reader for single band DEMs in EPSG:4326 (SRTM, Copernicus GLO-30, ASTER, ALOS, FABDEM
/// tiles as distributed). Projected rasters must be warped first:
///   gdalwarp -t_srs EPSG:4326 in.tif out.tif
/// Supports stripped and tiled layouts and an optional window so we never decode a whole 3 GB mosaic.
/// </summary>
public static class GeoTiffReader
{
    private const int TagPixelScale = 33550;
    private const int TagTiePoint = 33922;
    private const int TagGdalNoData = 42113;

    static GeoTiffReader() => Tiff.SetErrorHandler(new QuietTiffHandler());

    public static DemGrid Read(string path, BBox? window = null)
    {
        using var tif = Tiff.Open(path, "r") ?? throw new InvalidDataException($"cannot open {path} as TIFF");

        int width = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
        int height = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
        int bps = tif.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 16;
        int spp = tif.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;
        var fmt = (SampleFormat)(tif.GetField(TiffTag.SAMPLEFORMAT)?[0].ToInt() ?? (int)SampleFormat.UINT);

        var scale = ReadDoubles(tif, TagPixelScale) ?? throw new InvalidDataException("no ModelPixelScale tag - not a GeoTIFF?");
        var tie = ReadDoubles(tif, TagTiePoint) ?? throw new InvalidDataException("no ModelTiepoint tag - not a GeoTIFF?");
        double sx = scale[0], sy = scale[1];
        if (sx > 0.5 || sy > 0.5)
            throw new InvalidDataException("raster looks projected (pixel size in metres). Reproject to EPSG:4326 with gdalwarp first.");

        double west = tie[3] - tie[0] * sx;
        double north = tie[4] + tie[1] * sy;
        float noData = ReadNoData(tif) ?? -32768f;

        int c0 = 0, c1 = width, r0 = 0, r1 = height;
        if (window is { } w)
        {
            c0 = Math.Clamp((int)Math.Floor((w.West - west) / sx), 0, width);
            c1 = Math.Clamp((int)Math.Ceiling((w.East - west) / sx), 0, width);
            r0 = Math.Clamp((int)Math.Floor((north - w.North) / sy), 0, height);
            r1 = Math.Clamp((int)Math.Ceiling((north - w.South) / sy), 0, height);
            if (c1 - c0 < 2 || r1 - r0 < 2) throw new ArgumentException("requested window is outside the raster");
        }

        int nc = c1 - c0, nr = r1 - r0;
        var z = new float[nc * nr];
        int bytesPerSample = bps / 8;
        int pixelStride = bytesPerSample * spp;

        if (tif.IsTiled())
        {
            int tw = tif.GetField(TiffTag.TILEWIDTH)[0].ToInt();
            int th = tif.GetField(TiffTag.TILELENGTH)[0].ToInt();
            var buf = new byte[tif.TileSize()];
            for (int ty = r0 / th * th; ty < r1; ty += th)
                for (int tx = c0 / tw * tw; tx < c1; tx += tw)
                {
                    tif.ReadTile(buf, 0, tx, ty, 0, 0);
                    for (int yy = 0; yy < th; yy++)
                    {
                        int row = ty + yy;
                        if (row < r0 || row >= r1) continue;
                        for (int xx = 0; xx < tw; xx++)
                        {
                            int col = tx + xx;
                            if (col < c0 || col >= c1) continue;
                            z[(row - r0) * nc + (col - c0)] = Decode(buf, (yy * tw + xx) * pixelStride, bps, fmt);
                        }
                    }
                }
        }
        else
        {
            var line = new byte[tif.ScanlineSize()];
            for (int row = r0; row < r1; row++)
            {
                tif.ReadScanline(line, row);
                for (int col = c0; col < c1; col++)
                    z[(row - r0) * nc + (col - c0)] = Decode(line, col * pixelStride, bps, fmt);
            }
        }

        var grid = new DemGrid(nc, nr, west + c0 * sx, north - r0 * sy, sx, z);
        grid.FillNoData(noData);

        // non-square pixels (high latitude Copernicus tiles) -> resample onto square cells
        if (Math.Abs(sx - sy) / sx > 0.01)
            grid = Resquare(grid, sx, sy);
        return grid;
    }

    private static DemGrid Resquare(DemGrid src, double sx, double sy)
    {
        double cell = Math.Min(sx, sy);
        int nc = (int)Math.Round(src.Cols * sx / cell);
        int nr = (int)Math.Round(src.Rows * sy / cell);
        var z = new float[nc * nr];
        for (int r = 0; r < nr; r++)
            for (int c = 0; c < nc; c++)
            {
                int oc = Math.Min(src.Cols - 1, (int)(c * cell / sx));
                int or = Math.Min(src.Rows - 1, (int)(r * cell / sy));
                z[r * nc + c] = src[or, oc];
            }
        return new DemGrid(nc, nr, src.West, src.North, cell, z);
    }

    private static float Decode(byte[] b, int o, int bps, SampleFormat fmt) => (fmt, bps) switch
    {
        (SampleFormat.IEEEFP, 32) => BitConverter.ToSingle(b, o),
        (SampleFormat.IEEEFP, 64) => (float)BitConverter.ToDouble(b, o),
        (SampleFormat.INT, 16) => BitConverter.ToInt16(b, o),
        (SampleFormat.INT, 32) => BitConverter.ToInt32(b, o),
        (SampleFormat.UINT, 16) => BitConverter.ToUInt16(b, o),
        (SampleFormat.UINT, 32) => BitConverter.ToUInt32(b, o),
        (_, 8) => b[o],
        _ => throw new NotSupportedException($"sample format {fmt}/{bps} bit not supported")
    };

    private static double[]? ReadDoubles(Tiff tif, int tag)
    {
        var f = tif.GetField((TiffTag)tag);
        if (f == null) return null;
        // anonymous tags come back as [count, array]; be lenient about which slot holds the array
        foreach (var v in f)
        {
            if (v.Value is double[] d) return d;
            if (v.Value is byte[] bytes && bytes.Length >= 16 && bytes.Length % 8 == 0)
            {
                var arr = new double[bytes.Length / 8];
                Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
                return arr;
            }
        }
        return null;
    }

    private static float? ReadNoData(Tiff tif)
    {
        var f = tif.GetField((TiffTag)TagGdalNoData);
        if (f == null) return null;
        foreach (var v in f)
        {
            string? s = v.Value switch
            {
                string str => str,
                byte[] bytes => System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0'),
                _ => null
            };
            if (s != null && float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float nd))
                return nd;
        }
        return null;
    }

    private sealed class QuietTiffHandler : TiffErrorHandler
    {
        public override void WarningHandler(Tiff tif, string method, string format, params object[] args) { }
        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }
    }
}
