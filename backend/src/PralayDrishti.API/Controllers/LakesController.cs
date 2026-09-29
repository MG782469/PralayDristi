using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PralayDrishti.API.Data;
using PralayDrishti.API.Services;
using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Hydraulics;

namespace PralayDrishti.API.Controllers;

[ApiController]
[Route("api/lakes")]
public class LakesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LakeToAlertChain _chain;
    private readonly DataPaths _paths;

    public LakesController(AppDbContext db, LakeToAlertChain chain, DataPaths paths)
    {
        _db = db;
        _chain = chain;
        _paths = paths;
    }

    [HttpGet]
    public async Task<List<LakeDetectionEntity>> List() => await _db.Lakes.OrderByDescending(l => l.DetectedUtc).Take(200).ToListAsync();

    /// <summary>Endpoint the GEE watcher posts to (same JSON it drops in data/gee_outbox).</summary>
    [HttpPost("detections")]
    public async Task<IActionResult> Ingest([FromBody] System.Text.Json.Nodes.JsonNode body, bool? autoSimulate = null, CancellationToken ct = default)
    {
        List<LakeDetectionInput> list;
        try { list = LakeToAlertChain.ParseOutbox(body.ToJsonString()); }
        catch (Exception ex) { return BadRequest($"could not parse detections: {ex.Message}"); }
        if (list.Count == 0) return BadRequest("no detections");
        var ids = await _chain.IngestAsync(list, autoSimulate, ct);
        return Ok(new { ingested = ids.Count, ids });
    }

    [HttpPost("{id:guid}/simulate")]
    public async Task<IActionResult> Simulate(Guid id, CancellationToken ct)
    {
        try
        {
            var runId = await _chain.EstimateAndSimulateAsync(id, ct);
            return Ok(new { runId });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) { return UnprocessableEntity(ex.Message); }
    }

    [HttpPost("{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id)
    {
        var l = await _db.Lakes.FindAsync(id);
        if (l == null) return NotFound();
        l.State = "Dismissed";
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// Offline change detection when there is no GEE access: upload before/after NDWI GeoTIFFs (or green+NIR pairs)
    /// covering the same area. New water bodies go into the same chain.
    /// </summary>
    [HttpPost("ndwi")]
    [RequestSizeLimit(1_000_000_000)]
    public async Task<IActionResult> Ndwi(IFormFile before, IFormFile after, double threshold = 0.1, double minAreaKm2 = 0.02,
        bool autoSimulate = false, CancellationToken ct = default)
    {
        string b = await Save(before, ct), a = await Save(after, ct);
        try
        {
            var gb = GeoTiffReader.Read(b);
            var ga = GeoTiffReader.Read(a);
            if (gb.Cols != ga.Cols || gb.Rows != ga.Rows) return BadRequest("before/after rasters must be on the same grid");
            var mb = gb.Z.Select(v => v > threshold).ToArray();
            var ma = ga.Z.Select(v => v > threshold).ToArray();
            var found = LakeChangeDetector.Detect(ga, mb, ma, minAreaKm2);
            if (found.Count == 0) return Ok(new { detections = 0 });
            var inputs = found.Take(20).Select(f => new LakeDetectionInput(f.Centroid.Lat, f.Centroid.Lon, Math.Round(f.AreaKm2, 4),
                0, null, "Uploaded NDWI change detection")).ToList();
            var ids = await _chain.IngestAsync(inputs, autoSimulate, ct);
            return Ok(new { detections = ids.Count, ids, bodies = found.Take(20).Select(f => new { f.Centroid.Lat, f.Centroid.Lon, f.AreaKm2 }) });
        }
        finally
        {
            System.IO.File.Delete(b);
            System.IO.File.Delete(a);
        }
    }

    /// <summary>Water area from a single NDWI raster around a point - input for the satellite reservoir estimate.</summary>
    [HttpPost("water-area")]
    public async Task<IActionResult> WaterArea(IFormFile ndwi, double lon, double lat, double threshold = 0.1, CancellationToken ct = default)
    {
        string f = await Save(ndwi, ct);
        try
        {
            var g = GeoTiffReader.Read(f);
            var mask = g.Z.Select(v => v > threshold).ToArray();
            double area = SatelliteReservoirEstimator.WaterBodyAreaKm2(g, mask, new GeoPoint(lon, lat));
            return Ok(new { areaKm2 = Math.Round(area, 4) });
        }
        finally { System.IO.File.Delete(f); }
    }

    private async Task<string> Save(IFormFile file, CancellationToken ct)
    {
        string path = Path.Combine(_paths.Uploads, Guid.NewGuid().ToString("N") + ".tif");
        await using var fs = System.IO.File.Create(path);
        await file.CopyToAsync(fs, ct);
        return path;
    }
}
