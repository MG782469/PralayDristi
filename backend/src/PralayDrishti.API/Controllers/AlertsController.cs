using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PralayDrishti.API.Data;
using PralayDrishti.API.Services;

namespace PralayDrishti.API.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AlertsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _db.Alerts.OrderByDescending(a => a.CreatedUtc).Take(200)
        .Select(a => new { a.Id, a.RunId, a.CreatedUtc, a.Headline, a.Severity, a.Status, a.PopulationAtRisk, a.FirstArrivalMin, a.Acknowledged })
        .ToListAsync());

    /// <summary>The CAP 1.2 document, ready for NDMA SACHET or any CAP aggregator.</summary>
    [HttpGet("{id:guid}/cap")]
    public async Task<IActionResult> Cap(Guid id)
    {
        var a = await _db.Alerts.FindAsync(id);
        return a == null ? NotFound() : Content(a.CapXml, "application/cap+xml");
    }

    [HttpPost("{id:guid}/ack")]
    public async Task<IActionResult> Ack(Guid id)
    {
        var a = await _db.Alerts.FindAsync(id);
        if (a == null) return NotFound();
        a.Acknowledged = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>CAP feed (Atom) of the latest alerts, the usual way aggregators poll.</summary>
    [HttpGet("feed")]
    public async Task<IActionResult> Feed()
    {
        var alerts = await _db.Alerts.OrderByDescending(a => a.CreatedUtc).Take(50).ToListAsync();
        string baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<feed xmlns=\"http://www.w3.org/2005/Atom\">");
        sb.AppendLine("<title>PralayDrishti CAP alerts</title>");
        sb.AppendLine($"<id>{baseUrl}/api/alerts/feed</id>");
        sb.AppendLine($"<updated>{(alerts.FirstOrDefault()?.CreatedUtc ?? DateTime.UtcNow):yyyy-MM-ddTHH:mm:ssZ}</updated>");
        foreach (var a in alerts)
        {
            sb.AppendLine("<entry>");
            sb.AppendLine($"<id>urn:uuid:{a.Id}</id>");
            sb.AppendLine($"<title>{System.Security.SecurityElement.Escape(a.Headline)}</title>");
            sb.AppendLine($"<updated>{a.CreatedUtc:yyyy-MM-ddTHH:mm:ssZ}</updated>");
            sb.AppendLine($"<link rel=\"alternate\" type=\"application/cap+xml\" href=\"{baseUrl}/api/alerts/{a.Id}/cap\"/>");
            sb.AppendLine("</entry>");
        }
        sb.AppendLine("</feed>");
        return Content(sb.ToString(), "application/atom+xml");
    }
}

[ApiController]
[Route("api/atlas")]
public class AtlasController : ControllerBase
{
    private readonly AtlasService _atlas;

    public AtlasController(AtlasService atlas) => _atlas = atlas;

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] List<int>? damIds, CancellationToken ct)
    {
        var ids = await _atlas.QueueAllAsync(damIds is { Count: > 0 } ? damIds : null, ct);
        return Ok(new { queued = ids.Count, ids });
    }

    [HttpGet]
    public async Task<IActionResult> Leaderboard(CancellationToken ct) => Ok(await _atlas.LeaderboardAsync(ct));
}
