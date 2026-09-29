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

[Route("api/atlas")]
[Authorize(Policy = Policies.CanRead)]
public class AtlasController : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<LeaderboardRowDto>> Leaderboard(CancellationToken ct) => Sender.Send(new GetLeaderboardQuery(), ct);

    /// <summary>Queues a coarse run for every dam in the register (or the given ids).</summary>
    [HttpPost("run")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Run([FromBody] List<int>? damIds, CancellationToken ct)
    {
        var ids = await Sender.Send(new RunAtlasCommand(damIds), ct);
        return Ok(new { queued = ids.Count, ids });
    }
}
