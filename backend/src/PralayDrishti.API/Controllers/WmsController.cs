using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PralayDrishti.Application.Features.Queries.Wms.GetWms;

namespace PralayDrishti.API.Controllers;

/// <summary>Minimal OGC WMS 1.1.1 / 1.3.0. Anonymous because GIS clients (QGIS, Bhuvan) can't send a JWT.</summary>
[Route("wms")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class WmsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var q = Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var r = await Sender.Send(new GetWmsQuery(q, BaseUrl), ct);
        if (r.Status != 200) Response.StatusCode = r.Status;
        return File(r.Body, r.ContentType);
    }
}
