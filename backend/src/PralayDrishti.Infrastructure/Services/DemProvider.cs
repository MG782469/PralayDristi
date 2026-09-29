using Microsoft.Extensions.Logging;
using System.Text.Json;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.Infrastructure.Services;

public sealed record DemSource(DemGrid Dem, string Description, bool Synthetic);

/// <summary>
/// Where the terrain comes from, in order of preference:
///   1. a GeoTIFF the user uploaded / dropped in data/dem (Copernicus GLO-30, FABDEM, SRTM, CartoDEM ...)
///   2. AWS terrain tiles, fetched once and cached (works offline afterwards)
///   3. a synthetic valley - only so the demo never dead-ends; clearly labelled
/// </summary>
public sealed class DemProvider : IDemProvider
{
    private readonly DataPaths _paths;
    private readonly AppOptions _opt;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<DemProvider> _log;

    public DemProvider(DataPaths paths, AppOptions opt, IHttpClientFactory http, ILogger<DemProvider> log)
    {
        _paths = paths;
        _opt = opt;
        _http = http;
        _log = log;
    }

    public sealed record DemInfo(string File, double West, double South, double East, double North, int Cols, int Rows, double CellSize);

    public List<DemInfo> ListUploaded()
    {
        var list = new List<DemInfo>();
        foreach (var f in Directory.GetFiles(_paths.Dem, "*.tif*"))
        {
            try
            {
                // read a tiny window just to get the georeferencing... simplest is to read the header via a 2x2 window
                var g = GeoTiffReader.Read(f);
                list.Add(new DemInfo(Path.GetFileName(f), g.West, g.South, g.East, g.North, g.Cols, g.Rows, g.CellSize));
            }
            catch (Exception ex)
            {
                _log.LogWarning("Skipping DEM {File}: {Msg}", f, ex.Message);
            }
        }
        return list;
    }

    public async Task<DemSource> ForAreaAsync(GeoPoint center, double downstreamKm, double upstreamKm, int zoom, CancellationToken ct)
    {
        double radiusM = Math.Max(downstreamKm, upstreamKm) * 1000 * 0.85 + 3000;
        var box = BBox.Around(center, radiusM, radiusM);

        // 1. uploaded rasters
        foreach (var f in Directory.GetFiles(_paths.Dem, "*.tif*"))
        {
            try
            {
                var full = GeoTiffReader.Read(f, box);
                if (!full.Bounds.Contains(center)) continue;
                var corridor = ScenarioEngine.CorridorBounds(full.SuggestFactor(250_000) > 1 ? full.Coarsen(full.SuggestFactor(250_000)) : full,
                    center, downstreamKm, upstreamKm);
                return new DemSource(full.Crop(corridor), $"Uploaded DEM {Path.GetFileName(f)}", false);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                // not covering this area or unsupported - try the next one
            }
        }

        // 2. terrain tiles: coarse pass to trace the river, fine pass over the corridor only
        try
        {
            var tiles = new TerrainTileSource(_http.CreateClient("tiles"), _paths.Tiles, _opt.Offline);
            int coarseZoom = Math.Max(8, zoom - 3);
            var coarse = await tiles.FetchAsync(box, coarseZoom, ct);
            var corridor = ScenarioEngine.CorridorBounds(coarse, center, downstreamKm, upstreamKm);
            var fine = await tiles.FetchAsync(corridor, zoom, ct);
            return new DemSource(fine, $"AWS Terrain Tiles z{zoom} (SRTM-based, ~{TerrainTileSource.CellSizeForZoom(zoom) * 111000:F0} m)", false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _log.LogWarning("Terrain tiles unavailable ({Msg}); falling back to synthetic terrain", ex.Message);
        }

        // 3. synthetic
        var v = SyntheticTerrain.Build(center, 420, 240, 30);
        return new DemSource(v.Dem, "SYNTHETIC valley (no DEM available offline for this area - demo only)", true);
    }

    public Task<JsonPayload> ListUploadedAsync(CancellationToken ct) =>
        Task.FromResult(new JsonPayload(JsonSerializer.Serialize(ListUploaded(), Mappings.Json)));

    public async Task<string> SaveUploadAsync(Stream content, string fileName, CancellationToken ct)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        string dest = Path.Combine(_paths.Dem, $"{name}_{DateTime.UtcNow:yyyyMMddHHmmss}.tif");
        await using (var fs = File.Create(dest))
            await content.CopyToAsync(fs, ct);
        try
        {
            GeoTiffReader.Read(dest, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException)
        {
            File.Delete(dest);
            throw new BusinessRuleException($"Not a usable DEM: {ex.Message}");
        }
        return Path.GetFileName(dest);
    }
}
