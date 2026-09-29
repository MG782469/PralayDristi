using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PralayDrishti.API.Extensions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Features.Commands.Lakes.DetectFromNdwi;
using PralayDrishti.Application.Features.Commands.Lakes.DismissLake;
using PralayDrishti.Application.Features.Commands.Lakes.IngestDetections;
using PralayDrishti.Application.Features.Commands.Lakes.SimulateLake;
using PralayDrishti.Application.Features.Queries.Lakes.GetLakes;
using PralayDrishti.Application.Features.Queries.Lakes.GetWaterArea;
using PralayDrishti.Application.Services;

namespace PralayDrishti.API.Controllers;

[Route("api/lakes")]
[Authorize(Policy = Policies.CanRead)]
public class LakesController : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<LakeDto>> List(CancellationToken ct) => Sender.Send(new GetLakesQuery(), ct);

    /// <summary>What gee/lake_watch.py posts: {"detections":[{lat, lon, area_km2, ...}]} or a bare array.</summary>
    [HttpPost("detections")]
    [Authorize(Policy = Policies.CanSimulate)]
    public async Task<IActionResult> Ingest([FromBody] JsonNode body, CancellationToken ct, bool? autoSimulate = null)
    {
        var ids = await Sender.Send(new IngestDetectionsCommand(LakeToAlertService.ParseOutbox(body.ToJsonString()), autoSimulate), ct);
        return Ok(new { ingested = ids.Count, ids });
    }

    [HttpPost("{id:guid}/simulate")]
    [Authorize(Policy = Policies.CanSimulate)]
    public async Task<IActionResult> Simulate(Guid id, CancellationToken ct) => Ok(new { runId = await Sender.Send(new SimulateLakeCommand(id), ct) });

    [HttpPost("{id:guid}/dismiss")]
    [Authorize(Policy = Policies.CanSimulate)]
    public async Task<IActionResult> Dismiss(Guid id, CancellationToken ct)
    {
        await Sender.Send(new DismissLakeCommand(id), ct);
        return NoContent();
    }

    /// <summary>Offline change detection: before + after NDWI GeoTIFFs on the same grid.</summary>
    [HttpPost("ndwi")]
    [Authorize(Policy = Policies.CanSimulate)]
    [RequestSizeLimit(DependencyInjection.MaxUploadBytes)]
    public async Task<IActionResult> Ndwi(IFormFile before, IFormFile after, CancellationToken ct, double threshold = 0.1,
        double minAreaKm2 = 0.02, bool autoSimulate = false)
    {
        await using var b = before.OpenReadStream();
        await using var a = after.OpenReadStream();
        var ids = await Sender.Send(new DetectFromNdwiCommand(b, a, threshold, minAreaKm2, autoSimulate), ct);
        return Ok(new { detections = ids.Count, ids });
    }

    [HttpPost("water-area")]
    [RequestSizeLimit(DependencyInjection.MaxUploadBytes)]
    public async Task<IActionResult> WaterArea(IFormFile ndwi, double lon, double lat, CancellationToken ct, double threshold = 0.1)
    {
        await using var s = ndwi.OpenReadStream();
        return Ok(new { areaKm2 = await Sender.Send(new GetWaterAreaQuery(s, lon, lat, threshold), ct) });
    }
}
