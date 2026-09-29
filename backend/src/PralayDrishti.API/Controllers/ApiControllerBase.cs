using MediatR;
using Microsoft.AspNetCore.Mvc;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.API.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;

    protected ISender Sender => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    /// <summary>Engine-rendered JSON goes out as-is; re-serialising it would only cost time.</summary>
    protected ContentResult Json(JsonPayload payload) => Content(payload.Json, "application/json");

    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    protected string BaseUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
}
