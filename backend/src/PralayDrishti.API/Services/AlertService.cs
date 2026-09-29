using Microsoft.AspNetCore.SignalR;
using PralayDrishti.API.Data;
using PralayDrishti.API.Hubs;
using PralayDrishti.Infrastructure.Engine.Export;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.API.Services;

public sealed class AlertService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<RunHub> _hub;

    public AlertService(AppDbContext db, IHubContext<RunHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<AlertEntity> CreateFromRunAsync(Guid runId, ScenarioOutcome o, CancellationToken ct, string status = "Exercise")
    {
        var cap = CapAlertBuilder.Build(o, runId.ToString("N")[..12], new CapAlertBuilder.Options(Status: status,
            Probabilistic: o.Ensemble != null));
        var first = o.Impact.Places.FirstOrDefault(p => p.ArrivalMin != null);
        string where = o.Prepared.Spec.Mode == ScenarioMode.NaturalDam ? "landslide lake" : o.Prepared.Spec.Dam.Name;
        var alert = new AlertEntity
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            CreatedUtc = DateTime.UtcNow,
            Headline = first == null
                ? $"{where}: no settlement in the modelled flood zone"
                : $"{where}: flood reaches {first.Name} in ~{first.ArrivalMin:F0} min, {o.Impact.PopulationAtRisk:N0} people at risk",
            Severity = o.Impact.Places.Any(p => p.HazardClass >= 5) ? "Extreme" : o.Impact.Places.Any(p => p.HazardClass >= 3) ? "Severe" : "Moderate",
            Status = status,
            PopulationAtRisk = o.Impact.PopulationAtRisk,
            FirstArrivalMin = first?.ArrivalMin,
            CapXml = cap
        };
        if (alert.Headline.Length > 200) alert.Headline = alert.Headline[..200];
        _db.Alerts.Add(alert);

        var lake = _db.Lakes.FirstOrDefault(l => l.RunId == runId);
        if (lake != null) lake.State = "Alerted";
        await _db.SaveChangesAsync(ct);
        await _hub.Clients.All.SendAsync("alert", new { alert.Id, alert.RunId, alert.Headline, alert.Severity, alert.CreatedUtc }, ct);
        return alert;
    }
}
