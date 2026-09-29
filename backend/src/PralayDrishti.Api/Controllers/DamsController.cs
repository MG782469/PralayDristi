using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PralayDrishti.Api.Data;
using PralayDrishti.Api.Services;
using PralayDrishti.Core.Pipeline;

namespace PralayDrishti.Api.Controllers;

[ApiController]
[Route("api/dams")]
public class DamsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly RunQueue _queue;

    public DamsController(AppDbContext db, RunQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    [HttpGet]
    public async Task<List<DamEntity>> List() => await _db.Dams.OrderBy(d => d.Name).ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DamEntity>> Get(int id) => await _db.Dams.FindAsync(id) is { } d ? d : NotFound();

    [HttpPost]
    public async Task<ActionResult<DamEntity>> Create(DamEntity dam)
    {
        if (string.IsNullOrWhiteSpace(dam.Name)) return BadRequest("name is required");
        if (dam.HeightM <= 0) return BadRequest("height must be positive");
        dam.Id = 0;
        _db.Dams.Add(dam);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = dam.Id }, dam);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, DamEntity dam)
    {
        var existing = await _db.Dams.FindAsync(id);
        if (existing == null) return NotFound();
        dam.Id = id;
        _db.Entry(existing).CurrentValues.SetValues(dam);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var d = await _db.Dams.FindAsync(id);
        if (d == null) return NotFound();
        _db.Dams.Remove(d);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Default scenario for the dam, returned so the UI can pre-fill the form.</summary>
    [HttpGet("{id:int}/scenario")]
    public async Task<ActionResult<ScenarioSpec>> DefaultScenario(int id, bool cascade = false)
    {
        var d = await _db.Dams.FindAsync(id);
        if (d == null) return NotFound();
        return ScenarioFactory.Default(d, await _db.Dams.ToListAsync(), cascade);
    }

    [HttpPost("{id:int}/simulate")]
    public async Task<ActionResult<RunSummary>> Simulate(int id, bool cascade = false)
    {
        var d = await _db.Dams.FindAsync(id);
        if (d == null) return NotFound();
        var spec = ScenarioFactory.Default(d, await _db.Dams.ToListAsync(), cascade);
        var run = ScenarioFactory.NewRun(spec, "user", d.Id);
        _db.Runs.Add(run);
        await _db.SaveChangesAsync();
        await _queue.EnqueueAsync(run.Id);
        return RunViews.Summary(run);
    }
}
