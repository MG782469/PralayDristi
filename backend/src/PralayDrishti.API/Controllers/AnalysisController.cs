using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PralayDrishti.API.Extensions;
using PralayDrishti.Application.Features.Queries.Analysis.CalculateBreach;
using PralayDrishti.Application.Features.Queries.Analysis.EstimateNaturalDam;
using PralayDrishti.Application.Features.Queries.Analysis.EstimateReservoir;
using PralayDrishti.Application.Features.Queries.Analysis.InverseFromObserved;
using PralayDrishti.Application.Features.Queries.Analysis.InverseThreshold;

namespace PralayDrishti.API.Controllers;

[Route("api/analysis")]
[Authorize(Policy = Policies.CanSimulate)]
public class AnalysisController : ApiControllerBase
{
    /// <summary>Natural-dam mode: lake level, volume and AEV curve behind a drawn debris polygon.</summary>
    [HttpPost("natural-dam")]
    public async Task<ContentResult> NaturalDam(EstimateNaturalDamQuery q, CancellationToken ct) => Json(await Sender.Send(q, ct));

    /// <summary>Satellite-only reservoir volume (NDWI area or SWOT / ICESat-2 level + DEM).</summary>
    [HttpPost("reservoir-estimate")]
    public async Task<ContentResult> Reservoir(EstimateReservoirQuery q, CancellationToken ct) => Json(await Sender.Send(q, ct));

    [HttpPost("breach")]
    [Authorize(Policy = Policies.CanRead)]
    public async Task<ContentResult> Breach(CalculateBreachQuery q, CancellationToken ct) => Json(await Sender.Send(q, ct));

    /// <summary>Smallest release that floods a target (bridge / camp / highway) to a given depth.</summary>
    [HttpPost("inverse/threshold")]
    public async Task<ContentResult> Threshold(InverseThresholdQuery q, CancellationToken ct) => Json(await Sender.Send(q, ct));

    public sealed record ObservedRequest(Guid RunId, JsonObject Observed);

    /// <summary>Release that best reproduces a satellite-observed flood extent.</summary>
    [HttpPost("inverse/observed")]
    public async Task<ContentResult> Observed(ObservedRequest r, CancellationToken ct) =>
        Json(await Sender.Send(new InverseFromObservedQuery(r.RunId, r.Observed.ToJsonString()), ct));
}
