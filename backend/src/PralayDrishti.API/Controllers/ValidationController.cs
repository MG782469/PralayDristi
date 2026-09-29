using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PralayDrishti.API.Extensions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Features.Commands.Data.UploadDem;
using PralayDrishti.Application.Features.Commands.Data.UploadExposure;
using PralayDrishti.Application.Features.Commands.Validation.CompareWithObserved;
using PralayDrishti.Application.Features.Commands.Validation.RunBenchmark;
using PralayDrishti.Application.Features.Queries.Data.GetDems;
using PralayDrishti.Application.Features.Queries.Data.GetExposure;
using PralayDrishti.Application.Features.Queries.Data.GetSystemInfo;
using PralayDrishti.Application.Features.Queries.Data.GetTerrainTile;
using PralayDrishti.Application.Features.Queries.Validation.GetAnalyticCheck;
using PralayDrishti.Application.Features.Queries.Validation.GetScoreboard;
using PralayDrishti.Application.Features.Queries.Validation.GetSphCheck;
using PralayDrishti.Application.Features.Queries.Wms.GetWms;

namespace PralayDrishti.API.Controllers;

[Route("api/validation")]
[Authorize(Policy = Policies.CanRead)]
public class ValidationController : ApiControllerBase
{
    [HttpGet("analytic")]
    public async Task<ContentResult> Analytic(CancellationToken ct, double h0 = 10, double h1 = 0, double t = 8) =>
        Json(await Sender.Send(new GetAnalyticCheckQuery(h0, h1, t), ct));

    [HttpGet("sph")]
    public async Task<ContentResult> Sph(CancellationToken ct, int resolution = 20, double t = 1.0) =>
        Json(await Sender.Send(new GetSphCheckQuery(resolution, t), ct));

    [HttpGet("scoreboard")]
    public async Task<ContentResult> Scoreboard(CancellationToken ct) => Json(await Sender.Send(new GetScoreboardQuery(), ct));

    public sealed record CompareRequest(JsonObject Observed, JsonObject? PermanentWater, JsonArray? Arrivals, string? EventName,
        double DepthThreshold = 0.3);

    /// <summary>Simulated extent vs Sentinel-1 flood polygons: CSI / IoU, F1, POD, FAR, arrival error.</summary>
    [HttpPost("compare/{runId:guid}")]
    [Authorize(Policy = Policies.CanSimulate)]
    public async Task<ContentResult> Compare(Guid runId, CompareRequest r, CancellationToken ct) =>
        Json(await Sender.Send(new CompareWithObservedCommand(runId, r.Observed.ToJsonString(), r.PermanentWater?.ToJsonString(),
            r.Arrivals?.ToJsonString(), r.EventName, r.DepthThreshold), ct));

    [HttpPost("benchmark/{name}")]
    [Authorize(Policy = Policies.CanSimulate)]
    public async Task<ContentResult> Benchmark(string name, CancellationToken ct) => Json(await Sender.Send(new RunBenchmarkCommand(name), ct));
}
