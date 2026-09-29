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

[Route("api/users")]
[Authorize(Policy = Policies.AdminOnly)]
public class UsersController : ApiControllerBase
{
    public sealed record RoleRequest(UserRole Role);
    public sealed record ActiveRequest(bool Active);

    [HttpGet]
    public Task<IReadOnlyList<UserDto>> List(CancellationToken ct) => Sender.Send(new GetUsersQuery(), ct);

    [HttpPut("{id:guid}/role")]
    public Task<UserDto> Role(Guid id, RoleRequest r, CancellationToken ct) => Sender.Send(new ChangeUserRoleCommand(id, r.Role), ct);

    [HttpPut("{id:guid}/active")]
    public Task<UserDto> Active(Guid id, ActiveRequest r, CancellationToken ct) => Sender.Send(new SetUserActiveCommand(id, r.Active), ct);
}
