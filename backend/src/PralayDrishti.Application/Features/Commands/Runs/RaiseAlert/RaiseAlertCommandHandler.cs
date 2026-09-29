using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Common.Validation;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Runs.RaiseAlert;

public sealed class RaiseAlertCommandHandler : IRequestHandler<RaiseAlertCommand, AlertDto>
{
    private readonly ISimulationRunRepository _runs;
    private readonly AlertService _alerts;

    public RaiseAlertCommandHandler(ISimulationRunRepository runs, AlertService alerts)
    {
        _runs = runs;
        _alerts = alerts;
    }

    public async Task<AlertDto> Handle(RaiseAlertCommand request, CancellationToken ct)
    {
        var run = await _runs.GetByIdAsync(request.RunId, ct) ?? throw new NotFoundException("Run", request.RunId);
        if (run.Status != RunStatus.Completed) throw new BusinessRuleException("Alerts can only be raised for completed runs.");
        return await _alerts.CreateForRunAsync(run.Id, request.Status, ct);
    }
}
