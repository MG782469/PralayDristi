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

[Route("api")]
public class DataController : ApiControllerBase
{
    [HttpGet("system")]
    [AllowAnonymous]
    public Task<SystemInfoDto> System(CancellationToken ct) => Sender.Send(new GetSystemInfoQuery(), ct);

    [HttpGet("dem")]
    [Authorize(Policy = Policies.CanRead)]
    public async Task<ContentResult> Dems(CancellationToken ct) => Json(await Sender.Send(new GetDemsQuery(), ct));

    /// <summary>GeoTIFF DEM in EPSG:4326 (reproject with gdalwarp first).</summary>
    [HttpPost("dem/upload")]
    [Authorize(Policy = Policies.CanSimulate)]
    [RequestSizeLimit(DependencyInjection.MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = DependencyInjection.MaxUploadBytes)]
    public async Task<IActionResult> UploadDem(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return Ok(new { file = await Sender.Send(new UploadDemCommand(s, file.FileName), ct) });
    }

    [HttpGet("exposure")]
    [Authorize(Policy = Policies.CanRead)]
    public async Task<ContentResult> Exposure(double west, double south, double east, double north, CancellationToken ct, bool osm = false) =>
        Content(await Sender.Send(new GetExposureQuery(new BoundingBox(west, south, east, north), osm), ct), "application/geo+json");

    [HttpPost("exposure/upload")]
    [Authorize(Policy = Policies.CanSimulate)]
    [RequestSizeLimit(500_000_000)]
    public async Task<UploadExposureResult> UploadExposure(IFormFile file, CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        return await Sender.Send(new UploadExposureCommand(await reader.ReadToEndAsync(ct), file.FileName), ct);
    }

    /// <summary>Terrain tile proxy (cached) so the dashboard's 3D terrain works offline for simulated valleys.</summary>
    [HttpGet("tiles/terrarium/{z:int}/{x:int}/{y:int}.png")]
    [AllowAnonymous]
    [ResponseCache(Duration = 604800)]
    public async Task<IActionResult> Terrarium(int z, int x, int y, CancellationToken ct)
    {
        var png = await Sender.Send(new GetTerrainTileQuery(z, x, y), ct);
        return png == null ? NotFound() : File(png, "image/png");
    }
}
