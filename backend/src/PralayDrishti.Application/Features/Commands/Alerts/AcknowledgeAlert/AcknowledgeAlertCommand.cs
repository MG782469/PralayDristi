using MediatR;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Alerts.AcknowledgeAlert;

public sealed record AcknowledgeAlertCommand(Guid Id) : IRequest<Unit>;
