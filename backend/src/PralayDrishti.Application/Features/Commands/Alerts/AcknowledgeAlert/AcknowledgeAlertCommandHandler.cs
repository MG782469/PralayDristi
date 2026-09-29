using MediatR;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Alerts.AcknowledgeAlert;

public sealed class AcknowledgeAlertCommandHandler : IRequestHandler<AcknowledgeAlertCommand, Unit>
{
    private readonly IAlertRepository _alerts;
    private readonly IUnitOfWork _uow;

    public AcknowledgeAlertCommandHandler(IAlertRepository alerts, IUnitOfWork uow)
    {
        _alerts = alerts;
        _uow = uow;
    }

    public async Task<Unit> Handle(AcknowledgeAlertCommand request, CancellationToken ct)
    {
        var alert = await _alerts.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Alert", request.Id);
        alert.Acknowledged = true;
        await _uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
