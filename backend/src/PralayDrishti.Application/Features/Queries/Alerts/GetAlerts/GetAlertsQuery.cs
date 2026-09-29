using System.Text;
using MediatR;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Alerts.GetAlerts;

public sealed record GetAlertsQuery(int Take = 200) : IRequest<IReadOnlyList<AlertDto>>;
