using System.Text;
using MediatR;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Alerts.GetAlerts;

public sealed class GetAlertsQueryHandler : IRequestHandler<GetAlertsQuery, IReadOnlyList<AlertDto>>
{
    private readonly IAlertRepository _alerts;

    public GetAlertsQueryHandler(IAlertRepository alerts) => _alerts = alerts;

    public async Task<IReadOnlyList<AlertDto>> Handle(GetAlertsQuery request, CancellationToken ct) =>
        (await _alerts.ListAsync(Math.Clamp(request.Take, 1, 500), ct)).Select(a => a.ToDto()).ToList();
}
