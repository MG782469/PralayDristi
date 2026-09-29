using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PralayDrishti.API.Extensions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Features.Commands.Alerts.AcknowledgeAlert;
using PralayDrishti.Application.Features.Commands.Atlas.RunAtlas;
using PralayDrishti.Application.Features.Commands.Users.ChangeUserRole;
using PralayDrishti.Application.Features.Commands.Users.SetUserActive;
using PralayDrishti.Application.Features.Queries.Alerts.GetAlertCap;
using PralayDrishti.Application.Features.Queries.Alerts.GetAlertFeed;
using PralayDrishti.Application.Features.Queries.Alerts.GetAlerts;
using PralayDrishti.Application.Features.Queries.Atlas.GetLeaderboard;
using PralayDrishti.Application.Features.Queries.Users.GetUsers;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.API.Controllers;

[Route("api/alerts")]
[Authorize(Policy = Policies.CanRead)]
public class AlertsController : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AlertDto>> List(CancellationToken ct) => Sender.Send(new GetAlertsQuery(), ct);

    /// <summary>CAP 1.2 XML. Public: CAP is meant to be picked up by aggregators such as NDMA SACHET.</summary>
    [HttpGet("{id:guid}/cap")]
    [AllowAnonymous]
    [Produces("application/cap+xml")]
    public async Task<ContentResult> Cap(Guid id, CancellationToken ct) =>
        Content(await Sender.Send(new GetAlertCapQuery(id), ct), "application/cap+xml");

    [HttpGet("feed")]
    [AllowAnonymous]
    [Produces("application/atom+xml")]
    public async Task<ContentResult> Feed(CancellationToken ct) =>
        Content(await Sender.Send(new GetAlertFeedQuery(BaseUrl), ct), "application/atom+xml");

    [HttpPost("{id:guid}/ack")]
    [Authorize(Policy = Policies.CanSimulate)]
    public async Task<IActionResult> Ack(Guid id, CancellationToken ct)
    {
        await Sender.Send(new AcknowledgeAlertCommand(id), ct);
        return NoContent();
    }
}
