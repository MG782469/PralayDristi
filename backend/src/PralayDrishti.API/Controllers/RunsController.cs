using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PralayDrishti.API.Data;
using PralayDrishti.API.Services;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.API.Controllers;

[ApiController]
[Route("api/runs")]
public class RunsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RunQueue _queue;
    private readonly RunStore _store;

    public RunsController(AppDbContext db, RunQueue queue, RunStore store)
    {
        _db = db;
        _queue = queue;
        _store = store;
    }

    [HttpGet]
    public async Task<List<RunSummary>> List(string? origin = null, int take = 100)
    {
        var q = _db.Runs.AsQueryable();
        if (!string.IsNullOrEmpty(origin)) q = q.Where(r => r.Origin == origin);
        var runs = await q.OrderByDescending(r => r.CreatedUtc).Take(Math.Clamp(take, 1, 500)).ToListAsync();
        return runs.Select(RunViews.Summary).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<RunSummary>> Create(ScenarioSpec spec, int? damId = null)
    {
        var problems = Validate(spec);
        if (problems.Count > 0) return ValidationProblem(new ValidationProblemDetails(problems));
        var run = ScenarioFactory.NewRun(spec, "user", damId);
        _db.Runs.Add(run);
        await _db.SaveChangesAsync();
        await _queue.EnqueueAsync(run.Id);
        return CreatedAtAction(nameof(Get), new { id = run.Id }, RunViews.Summary(run));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var run = await _db.Runs.FindAsync(id);
        if (run == null) return NotFound();
        if (run.Status != RunStatus.Completed) return Ok(new { summary = RunViews.Summary(run) });
        var o = _store.Get(id);
        if (o == null) return Ok(new { summary = RunViews.Summary(run), error = "result files missing" });
        return Ok(RunViews.Detail(run, o));
    }

    [HttpGet("{id:guid}/status")]
    public async Task<ActionResult<RunSummary>> Status(Guid id) =>
        await _db.Runs.FindAsync(id) is { } r ? RunViews.Summary(r) : NotFound();

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var run = await _db.Runs.FindAsync(id);
        if (run == null) return NotFound();
        if (run.Status == RunStatus.Queued)
        {
            run.Status = RunStatus.Cancelled;
            run.Stage = "Cancelled";
            await _db.SaveChangesAsync();
            return Ok();
        }
        return _queue.Cancel(id) ? Ok() : Conflict("run is not active");
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var run = await _db.Runs.FindAsync(id);
        if (run == null) return NotFound();
        _queue.Cancel(id);
        _db.Runs.Remove(run);
        await _db.SaveChangesAsync();
        _store.Delete(id);
        return NoContent();
    }

    /// <summary>Re-run the same spec (e.g. after editing a dam), optionally with tweaks from the body.</summary>
    [HttpPost("{id:guid}/rerun")]
    public async Task<ActionResult<RunSummary>> Rerun(Guid id)
    {
        var run = await _db.Runs.FindAsync(id);
        if (run == null) return NotFound();
        var spec = System.Text.Json.JsonSerializer.Deserialize<ScenarioSpec>(run.SpecJson, RunArchive.Json)!;
        var copy = ScenarioFactory.NewRun(spec, run.Origin, run.DamId);
        _db.Runs.Add(copy);
        await _db.SaveChangesAsync();
        await _queue.EnqueueAsync(copy.Id);
        return RunViews.Summary(copy);
    }

    [HttpGet("{id:guid}/export/{format}")]
    public async Task<IActionResult> Export(Guid id, string format)
    {
        var run = await _db.Runs.FindAsync(id);
        if (run is not { Status: RunStatus.Completed }) return NotFound("run not completed");
        var o = _store.Get(id);
        if (o == null) return NotFound();
        try
        {
            var (data, type, name) = await Task.Run(() => _store.Export(id, o, format));
            return File(data, type, name);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/alert")]
    public async Task<IActionResult> RaiseAlert(Guid id, [FromServices] AlertService alerts, string status = "Exercise")
    {
        var o = _store.Get(id);
        if (o == null) return NotFound();
        if (status is not ("Actual" or "Exercise" or "Test")) return BadRequest("status must be Actual, Exercise or Test");
        var a = await alerts.CreateFromRunAsync(id, o, HttpContext.RequestAborted, status);
        return Ok(new { a.Id, a.Headline, a.Severity, a.Status });
    }

    /// <summary>
    /// Whole-grid PNG of a layer in EPSG:4326 (one pixel per cell). The dashboard uses these as image overlays -
    /// one request per animation frame instead of a tile pyramid, so the flood animation stays smooth.
    /// </summary>
    [HttpGet("{id:guid}/image/{layer}.png")]
    public IActionResult Image(Guid id, string layer)
    {
        var o = _store.Get(id);
        if (o == null) return NotFound();
        if (!WmsController.TryLayer(o, layer, out var values, out var kind)) return NotFound($"no layer {layer}");
        var png = PralayDrishti.Infrastructure.Engine.Export.RasterRenderer.RenderGrid(o.FarField.Dem, values, kind, o.Prepared.ExcludeMask);
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(png, "image/png");
    }

    /// <summary>Point query for the map: click anywhere and get depth / velocity / arrival / hazard there.</summary>
    [HttpGet("{id:guid}/sample")]
    public IActionResult Sample(Guid id, double lon, double lat)
    {
        var o = _store.Get(id);
        if (o == null) return NotFound();
        var g = o.FarField.Dem;
        if (!g.TryCell(new GeoPoint(lon, lat), out int r, out int c)) return Ok(new { inside = false });
        int i = g.Idx(r, c);
        float arr = o.FarField.ArrivalTime[i];
        return Ok(new
        {
            inside = true,
            ground = Math.Round(o.Prepared.OriginalDem.Z[i], 1),
            depth = Math.Round(o.FarField.MaxDepth[i], 2),
            velocity = Math.Round(o.FarField.MaxSpeed[i], 2),
            surface = float.IsNaN(o.FarField.MaxSurface[i]) ? (double?)null : Math.Round(o.FarField.MaxSurface[i], 1),
            arrivalMin = float.IsNaN(arr) ? (double?)null : Math.Round(arr / 60, 1),
            peakMin = Math.Round(o.FarField.PeakTime[i] / 60, 1),
            hazard = o.HazardClass[i],
            probability = o.Ensemble is { } e ? Math.Round(e.Probability[i], 2) : (double?)null,
            excluded = o.Prepared.ExcludeMask[i],
            depthSeries = o.FarField.Snapshots.Select(s => new[] { Math.Round(s.Time / 60, 1), Math.Round(s.Depth[i], 2) })
        });
    }

    /// <summary>Longitudinal profile along the traced river: bed, max water surface, arrival.</summary>
    [HttpGet("{id:guid}/profile")]
    public IActionResult Profile(Guid id)
    {
        var o = _store.Get(id);
        if (o == null) return NotFound();
        var p = o.Prepared;
        var (ch, bed) = Hydrology.Profile(p.OriginalDem, p.DownstreamPath);
        var f = o.FarField;
        int step = Math.Max(1, ch.Length / 400);
        var rows = new List<object>();
        for (int k = 0; k < ch.Length; k += step)
        {
            int i = p.DownstreamPath[k];
            rows.Add(new
            {
                km = Math.Round(ch[k] / 1000, 3),
                bed = Math.Round(bed[k], 1),
                ws = float.IsNaN(f.MaxSurface[i]) ? (double?)null : Math.Round(f.MaxSurface[i], 1),
                depth = Math.Round(f.MaxDepth[i], 2),
                velocity = Math.Round(f.MaxSpeed[i], 2),
                arrivalMin = float.IsNaN(f.ArrivalTime[i]) ? (double?)null : Math.Round(f.ArrivalTime[i] / 60, 1)
            });
        }
        return Ok(rows);
    }

    private static Dictionary<string, string[]> Validate(ScenarioSpec s)
    {
        var e = new Dictionary<string, string[]>();
        if (s.Mode == ScenarioMode.NaturalDam)
        {
            if (s.NaturalDam == null || s.NaturalDam.DebrisPolygon.Count < 3)
                e["naturalDam.debrisPolygon"] = new[] { "draw the debris/landslide footprint (at least 3 points)" };
        }
        else
        {
            if (s.Dam.HeightM <= 0) e["dam.heightM"] = new[] { "must be positive" };
            if (s.Dam.ReservoirVolumeM3 <= 0) e["dam.reservoirVolumeM3"] = new[] { "must be positive" };
            if (Math.Abs(s.Dam.Lat) > 90 || Math.Abs(s.Dam.Lon) > 180) e["dam.location"] = new[] { "invalid coordinates" };
        }
        if (s.DurationHours is <= 0 or > 72) e["durationHours"] = new[] { "0-72 h" };
        if (s.MaxCells is < 5_000 or > 3_000_000) e["maxCells"] = new[] { "5,000 - 3,000,000" };
        if (s.EnsembleMembers is < 0 or > 100) e["ensembleMembers"] = new[] { "0-100" };
        if (s.DownstreamKm is <= 0 or > 300) e["downstreamKm"] = new[] { "1-300 km" };
        return e;
    }
}
