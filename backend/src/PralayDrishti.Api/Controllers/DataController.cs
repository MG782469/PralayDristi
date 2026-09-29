using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PralayDrishti.Api.Data;
using PralayDrishti.Api.Services;
using PralayDrishti.Core.Analysis;
using PralayDrishti.Core.Geo;

namespace PralayDrishti.Api.Controllers;

[ApiController]
[Route("api/dem")]
public class DemController : ControllerBase
{
    private readonly DemProvider _dem;

    public DemController(DemProvider dem) => _dem = dem;

    [HttpGet]
    public List<DemProvider.DemInfo> List() => _dem.ListUploaded();

    /// <summary>Upload a GeoTIFF DEM in EPSG:4326 (Copernicus GLO-30, FABDEM, SRTM, ALOS, CartoDEM...).</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(4_000_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 4_000_000_000)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        try
        {
            string name = await _dem.SaveUploadAsync(file, ct);
            return Ok(new { file = name });
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            return BadRequest(ex.Message);
        }
    }
}

/// <summary>
/// Terrain tile proxy with the same on-disk cache the simulations use - the dashboard's 3D terrain and hillshade
/// keep working on an air-gapped laptop for every valley that has been simulated once.
/// </summary>
[ApiController]
[Route("api/tiles")]
public class TilesController : ControllerBase
{
    private readonly DataPaths _paths;
    private readonly AppOptions _opt;
    private readonly IHttpClientFactory _http;

    public TilesController(DataPaths paths, AppOptions opt, IHttpClientFactory http)
    {
        _paths = paths;
        _opt = opt;
        _http = http;
    }

    [HttpGet("terrarium/{z:int}/{x:int}/{y:int}.png")]
    public async Task<IActionResult> Terrarium(int z, int x, int y, CancellationToken ct)
    {
        if (z < 0 || z > 15 || x < 0 || y < 0 || x >= (1 << z) || y >= (1 << z)) return BadRequest();
        string file = Path.Combine(_paths.Tiles, z.ToString(), x.ToString(), $"{y}.png");
        if (!System.IO.File.Exists(file))
        {
            if (_opt.Offline) return NotFound();
            try
            {
                var bytes = await _http.CreateClient("tiles").GetByteArrayAsync($"https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png", ct);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await System.IO.File.WriteAllBytesAsync(file, bytes, ct);
            }
            catch (HttpRequestException)
            {
                return NotFound();
            }
        }
        Response.Headers.CacheControl = "public, max-age=604800";
        return PhysicalFile(file, "image/png");
    }
}

[ApiController]
[Route("api/exposure")]
public class ExposureController : ControllerBase
{
    private readonly ExposureProvider _exposure;
    private readonly DataPaths _paths;

    public ExposureController(ExposureProvider exposure, DataPaths paths)
    {
        _exposure = exposure;
        _paths = paths;
    }

    /// <summary>Exposure inside a box as GeoJSON, for the map (points + roads). Pulls OSM when online.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(double west, double south, double east, double north, bool osm = false, CancellationToken ct = default)
    {
        if (east - west > 2 || north - south > 2) return BadRequest("box too large (max 2 x 2 degrees)");
        var box = new BBox(west, south, east, north);
        var set = osm ? (await _exposure.ForAreaAsync(box, ct)).set : _exposure.LoadLocal(box);
        var features = new JsonArray();
        foreach (var p in set.Points)
            features.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["geometry"] = new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(p.Location.Lon, p.Location.Lat) },
                ["properties"] = new JsonObject { ["name"] = p.Name, ["kind"] = p.Kind.ToString(), ["population"] = p.Population, ["source"] = p.Source }
            });
        foreach (var r in set.Roads)
            features.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["geometry"] = new JsonObject { ["type"] = "LineString", ["coordinates"] = new JsonArray(r.Points.Select(p => (JsonNode)new JsonArray(p.Lon, p.Lat)).ToArray()) },
                ["properties"] = new JsonObject { ["name"] = r.Name, ["kind"] = "Road", ["category"] = r.Category }
            });
        return Content(new JsonObject { ["type"] = "FeatureCollection", ["features"] = features }.ToJsonString(), "application/geo+json");
    }

    /// <summary>Upload settlements / critical facilities / building footprints as GeoJSON (see ExposureProvider.ParseGeoJson).</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(500_000_000)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        using var sr = new StreamReader(file.OpenReadStream());
        string text = await sr.ReadToEndAsync(ct);
        JsonNode? node;
        try { node = JsonNode.Parse(text); }
        catch (Exception ex) { return BadRequest($"not JSON: {ex.Message}"); }
        if (node?["type"]?.GetValue<string>() != "FeatureCollection") return BadRequest("expected a GeoJSON FeatureCollection");
        var set = ExposureProvider.ParseGeoJson(node);
        string name = Path.GetFileNameWithoutExtension(file.FileName);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        await System.IO.File.WriteAllTextAsync(Path.Combine(_paths.Exposure, $"{name}.geojson"), text, ct);
        return Ok(new { points = set.Points.Count, roads = set.Roads.Count, buildings = set.Buildings.Count });
    }
}

[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    private readonly AppOptions _opt;
    private readonly DataPaths _paths;
    private readonly AppDbContext _db;
    private readonly RunQueue _queue;

    public SystemController(AppOptions opt, DataPaths paths, AppDbContext db, RunQueue queue)
    {
        _opt = opt;
        _paths = paths;
        _db = db;
        _queue = queue;
    }

    [HttpGet]
    public async Task<IActionResult> Info()
    {
        long cache = Directory.Exists(_paths.Tiles)
            ? new DirectoryInfo(_paths.Tiles).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
        return Ok(new
        {
            name = "PralayDrishti",
            version = typeof(SystemController).Assembly.GetName().Version?.ToString(),
            offline = _opt.Offline,
            osm = _opt.UseOsm && !_opt.Offline,
            delft3d = !string.IsNullOrWhiteSpace(_opt.Delft3DCommand),
            lakeWatch = _opt.LakeWatch.Enabled,
            runningRuns = _queue.RunningCount,
            dams = await _db.Dams.CountAsync(),
            runs = await _db.Runs.CountAsync(),
            alerts = await _db.Alerts.CountAsync(),
            terrainCacheMb = Math.Round(cache / 1024.0 / 1024.0, 1),
            exportFormats = RunStore.Formats,
            machine = Environment.MachineName,
            cores = Environment.ProcessorCount
        });
    }
}
