using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Services;

public sealed class AlertService
{
    private readonly IAlertRepository _alerts;
    private readonly ILakeDetectionRepository _lakes;
    private readonly IUnitOfWork _uow;
    private readonly IFloodSimulationEngine _engine;
    private readonly IRunNotifier _notifier;
    private readonly IDateTimeProvider _clock;

    public AlertService(IAlertRepository alerts, ILakeDetectionRepository lakes, IUnitOfWork uow, IFloodSimulationEngine engine,
        IRunNotifier notifier, IDateTimeProvider clock)
    {
        _alerts = alerts;
        _lakes = lakes;
        _uow = uow;
        _engine = engine;
        _notifier = notifier;
        _clock = clock;
    }

    /// <summary>
    /// Issues a CAP alert for a finished run. Status defaults to Exercise everywhere - a person has to choose Actual.
    /// </summary>
    public async Task<AlertDto> CreateForRunAsync(Guid runId, AlertStatus status, CancellationToken ct)
    {
        var draft = _engine.DraftAlert(runId);
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            CreatedUtc = _clock.UtcNow,
            Headline = draft.Headline,
            Severity = draft.Severity,
            Status = status,
            PopulationAtRisk = draft.PopulationAtRisk,
            FirstArrivalMin = draft.FirstArrivalMin,
            CapXml = _engine.BuildCap(runId, status, probabilistic: false)
        };
        await _alerts.AddAsync(alert, ct);

        var lake = await _lakes.GetByRunIdAsync(runId, ct);
        if (lake != null) lake.State = LakeState.Alerted;
        await _uow.SaveChangesAsync(ct);

        var dto = alert.ToDto();
        await _notifier.AlertAsync(dto);
        return dto;
    }
}
