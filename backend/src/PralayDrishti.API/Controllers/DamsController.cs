using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PralayDrishti.API.Extensions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Features.Commands.Dams.CreateDam;
using PralayDrishti.Application.Features.Commands.Dams.DeleteDam;
using PralayDrishti.Application.Features.Commands.Dams.SimulateDam;
using PralayDrishti.Application.Features.Commands.Dams.UpdateDam;
using PralayDrishti.Application.Features.Queries.Dams.GetDamById;
using PralayDrishti.Application.Features.Queries.Dams.GetDams;
using PralayDrishti.Application.Features.Queries.Dams.GetDefaultScenario;

namespace PralayDrishti.API.Controllers;

[Route("api/dams")]
[Authorize(Policy = Policies.CanRead)]
public class DamsController : ApiControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<DamDto>> List(CancellationToken ct) => Sender.Send(new GetDamsQuery(), ct);

    [HttpGet("{id:int}")]
    public Task<DamDto> Get(int id, CancellationToken ct) => Sender.Send(new GetDamByIdQuery(id), ct);

    /// <summary>Default scenario for the dam, so the dashboard can pre-fill the form.</summary>
    [HttpGet("{id:int}/scenario")]
    public Task<ScenarioSpec> Scenario(int id, bool cascade, CancellationToken ct) => Sender.Send(new GetDefaultScenarioQuery(id, cascade), ct);

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<DamDto>> Create(DamDto dam, CancellationToken ct)
    {
        var created = await Sender.Send(new CreateDamCommand(dam), ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public Task<DamDto> Update(int id, DamDto dam, CancellationToken ct) => Sender.Send(new UpdateDamCommand(id, dam), ct);

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await Sender.Send(new DeleteDamCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/simulate")]
    [Authorize(Policy = Policies.CanSimulate)]
    public Task<RunSummaryDto> Simulate(int id, bool cascade, CancellationToken ct) => Sender.Send(new SimulateDamCommand(id, cascade), ct);
}
