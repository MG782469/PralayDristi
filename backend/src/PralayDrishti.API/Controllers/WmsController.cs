using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PralayDrishti.API.Data;
using PralayDrishti.API.Services;
using PralayDrishti.Infrastructure.Engine.Export;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.API.Controllers;

/// <summary>
/// Minimal OGC WMS 1.1.1 / 1.3.0 so results load straight into Bhuvan / NDEM / QGIS / GeoServer cascades.
/// Layer names: {runId}:depth | hazard | arrival | velocity | probability | p90 | snapshot-{k}
/// </summary>
[ApiController]
[Route("wms")]
public class WmsController : ControllerBase
{
    private readonly RunStore _store;
    private readonly AppDbContext _db;

    public WmsController(RunStore store, AppDbContext db)
    {
        _store = store;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in Request.Query) q[k] = v.ToString();
        string request = q.GetValueOrDefault("REQUEST", "GetCapabilities");
        return request.ToLowerInvariant() switch
        {
            "getcapabilities" => Content(await Capabilities(q.GetValueOrDefault("VERSION", "1.3.0")), "text/xml"),
            "getmap" => GetMap(q),
            _ => ServiceException($"Request {request} not supported")
        };
    }

    private IActionResult GetMap(Dictionary<string, string> q)
    {
        string version = q.GetValueOrDefault("VERSION", "1.3.0");
        string crs = (q.GetValueOrDefault("CRS") ?? q.GetValueOrDefault("SRS") ?? "EPSG:4326").ToUpperInvariant();
        string layer = q.GetValueOrDefault("LAYERS", "");
        var parts = layer.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var id)) return ServiceException("LAYERS must be {runId}:{layer}");
        var o = _store.Get(id);
        if (o == null) return ServiceException("unknown run", 404);

        var b = (q.GetValueOrDefault("BBOX") ?? "").Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        if (b.Length != 4) return ServiceException("BBOX required");
        bool merc = crs is "EPSG:3857" or "EPSG:900913";
        // WMS 1.3.0 + EPSG:4326 uses lat,lon axis order
        var box = !merc && version == "1.3.0" && crs == "EPSG:4326" ? new BBox(b[1], b[0], b[3], b[2]) : new BBox(b[0], b[1], b[2], b[3]);
        int w = int.Parse(q.GetValueOrDefault("WIDTH", "256")), h = int.Parse(q.GetValueOrDefault("HEIGHT", "256"));

        if (!TryLayer(o, parts[1], out var values, out var kind)) return ServiceException($"unknown layer {parts[1]}");
        var png = RasterRenderer.RenderView(o.FarField.Dem, values, kind, box, merc, w, h, o.Prepared.ExcludeMask);
        Response.Headers.CacheControl = "public, max-age=3600";
        return File(png, "image/png");
    }

    public static bool TryLayer(ScenarioOutcome o, string name, out float[] values, out LayerKind kind)
    {
        var f = o.FarField;
        kind = LayerKind.Depth;
        values = Array.Empty<float>();
        switch (name.ToLowerInvariant())
        {
            case "depth": values = f.MaxDepth; return true;
            case "velocity": values = f.MaxSpeed; kind = LayerKind.Velocity; return true;
            case "hazard": values = o.HazardClass.Select(x => (float)x).ToArray(); kind = LayerKind.Hazard; return true;
            case "arrival":
                values = f.ArrivalTime.Select((a, i) => f.MaxDepth[i] < 0.1 ? float.NaN : a).ToArray();
                kind = LayerKind.Arrival;
                return true;
            case "probability" when o.Ensemble != null: values = o.Ensemble.Probability; kind = LayerKind.Probability; return true;
            case "p90" when o.Ensemble != null: values = o.Ensemble.P90Depth; return true;
        }
        if (name.StartsWith("snapshot-", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name[9..], out int k) && k >= 0 && k < f.Snapshots.Count)
        {
            values = f.Snapshots[k].Depth;
            kind = LayerKind.Snapshot;
            return true;
        }
        return false;
    }

    private async Task<string> Capabilities(string version)
    {
        var runs = await _db.Runs.Where(r => r.Status == RunStatus.Completed).OrderByDescending(r => r.FinishedUtc).Take(50).ToListAsync();
        string url = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/wms?";
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<WMS_Capabilities version=\"{version}\" xmlns=\"http://www.opengis.net/wms\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">");
        sb.AppendLine("<Service><Name>WMS</Name><Title>PralayDrishti dam-break inundation</Title><Abstract>Modelled flood depth, hazard, arrival time and probability</Abstract>");
        sb.AppendLine($"<OnlineResource xlink:href=\"{url}\"/></Service>");
        sb.AppendLine("<Capability><Request>");
        sb.AppendLine($"<GetCapabilities><Format>text/xml</Format><DCPType><HTTP><Get><OnlineResource xlink:href=\"{url}\"/></Get></HTTP></DCPType></GetCapabilities>");
        sb.AppendLine($"<GetMap><Format>image/png</Format><DCPType><HTTP><Get><OnlineResource xlink:href=\"{url}\"/></Get></HTTP></DCPType></GetMap>");
        sb.AppendLine("</Request><Exception><Format>XML</Format></Exception>");
        sb.AppendLine("<Layer><Title>PralayDrishti runs</Title><CRS>EPSG:4326</CRS><CRS>EPSG:3857</CRS>");
        foreach (var r in runs)
        {
            var o = _store.Get(r.Id);
            if (o == null) continue;
            var g = o.FarField.Dem;
            string bb = string.Create(CultureInfo.InvariantCulture,
                $"<EX_GeographicBoundingBox><westBoundLongitude>{g.West}</westBoundLongitude><eastBoundLongitude>{g.East}</eastBoundLongitude><southBoundLatitude>{g.South}</southBoundLatitude><northBoundLatitude>{g.North}</northBoundLatitude></EX_GeographicBoundingBox>");
            var names = new List<string> { "depth", "hazard", "arrival", "velocity" };
            if (o.Ensemble != null) names.AddRange(new[] { "probability", "p90" });
            foreach (var n in names)
                sb.AppendLine($"<Layer queryable=\"0\"><Name>{r.Id}:{n}</Name><Title>{System.Security.SecurityElement.Escape(r.Name)} - {n}</Title>{bb}</Layer>");
        }
        sb.AppendLine("</Layer></Capability></WMS_Capabilities>");
        return sb.ToString();
    }

    private ContentResult ServiceException(string msg, int code = 400)
    {
        Response.StatusCode = code;
        return Content($"<?xml version=\"1.0\"?><ServiceExceptionReport version=\"1.3.0\"><ServiceException>{System.Security.SecurityElement.Escape(msg)}</ServiceException></ServiceExceptionReport>", "text/xml");
    }
}
