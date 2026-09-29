using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PralayDrishti.API.Extensions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Features.Commands.Runs.CancelRun;
using PralayDrishti.Application.Features.Commands.Runs.DeleteRun;
using PralayDrishti.Application.Features.Commands.Runs.RaiseAlert;
using PralayDrishti.Application.Features.Commands.Runs.RerunSimulation;
using PralayDrishti.Application.Features.Commands.Runs.StartSimulation;
using PralayDrishti.Application.Features.Queries.Runs.ExportRun;
using PralayDrishti.Application.Features.Queries.Runs.GetLayerImage;
using PralayDrishti.Application.Features.Queries.Runs.GetRiverProfile;
using PralayDrishti.Application.Features.Queries.Runs.GetRunDetail;
using PralayDrishti.Application.Features.Queries.Runs.GetRuns;
using PralayDrishti.Application.Features.Queries.Runs.GetRunStatus;
using PralayDrishti.Application.Features.Queries.Runs.SamplePoint;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.API.Controllers;

[Route("api/runs")]
[Authorize(Policy = Policies.CanRead)]
public class RunsController : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RunSummaryDto>> List(RunOrigin? origin, CancellationToken ct, int take = 100) =>
        Sender.Send(new GetRunsQuery(origin, take), ct);

    [HttpPost]
    [Authorize(Policy = Policies.CanSimulate)]
    public async Task<ActionResult<RunSummaryDto>> Start(ScenarioSpec spec, int? damId, CancellationToken ct)
    {
        var run = await Sender.Send(new StartSimulationCommand(spec, damId), ct);
        return CreatedAtAction(nameof(Get), new { id = run.Id }, run);
    }

    /// <summary>Full result once completed; only the summary while queued / running.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ContentResult> Get(Guid id, CancellationToken ct) => Json(await Sender.Send(new GetRunDetailQuery(id), ct));

    [HttpGet("{id:guid}/status")]
    public Task<RunSummaryDto> Status(Guid id, CancellationToken ct) => Sender.Send(new GetRunStatusQuery(id), ct);

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.CanSimulate)]
    public Task<RunSummaryDto> Cancel(Guid id, CancellationToken ct) => Sender.Send(new CancelRunCommand(id), ct);

    [HttpPost("{id:guid}/rerun")]
    [Authorize(Policy = Policies.CanSimulate)]
    public Task<RunSummaryDto> Rerun(Guid id, CancellationToken ct) => Sender.Send(new RerunSimulationCommand(id), ct);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.CanSimulate)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await Sender.Send(new DeleteRunCommand(id), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/export/{format}")]
    [Produces("application/octet-stream")]
    public async Task<FileContentResult> Export(Guid id, string format, CancellationToken ct)
    {
        var f = await Sender.Send(new ExportRunQuery(id, format), ct);
        return File(f.Data, f.ContentType, f.FileName);
    }

    /// <summary>CAP alert from this run. Status defaults to Exercise; Actual is for real events only.</summary>
    [HttpPost("{id:guid}/alert")]
    [Authorize(Policy = Policies.CanSimulate)]
    public Task<AlertDto> RaiseAlert(Guid id, CancellationToken ct, AlertStatus status = AlertStatus.Exercise) =>
        Sender.Send(new RaiseAlertCommand(id, status), ct);

    /// <summary>Point query: depth / velocity / arrival / hazard at a clicked location.</summary>
    [HttpGet("{id:guid}/sample")]
    public async Task<ContentResult> Sample(Guid id, double lon, double lat, CancellationToken ct) =>
        Json(await Sender.Send(new SamplePointQuery(id, lon, lat), ct));

    /// <summary>Longitudinal profile along the traced river.</summary>
    [HttpGet("{id:guid}/profile")]
    public async Task<ContentResult> Profile(Guid id, CancellationToken ct) => Json(await Sender.Send(new GetRiverProfileQuery(id), ct));

    /// <summary>Whole-grid PNG of a layer (EPSG:4326). Anonymous so map image sources can load it; run ids are unguessable.</summary>
    [HttpGet("{id:guid}/image/{layer}.png")]
    [AllowAnonymous]
    [ResponseCache(Duration = 86400)]
    public async Task<FileContentResult> Image(Guid id, string layer, CancellationToken ct) =>
        File(await Sender.Send(new GetLayerImageQuery(id, layer), ct), "image/png");
}
