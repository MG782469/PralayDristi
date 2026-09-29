using System.Text;
using MediatR;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Alerts.GetAlertCap;

public sealed class GetAlertCapQueryHandler : IRequestHandler<GetAlertCapQuery, string>
{
    private readonly IAlertRepository _alerts;

    public GetAlertCapQueryHandler(IAlertRepository alerts) => _alerts = alerts;

    public async Task<string> Handle(GetAlertCapQuery request, CancellationToken ct) =>
        (await _alerts.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Alert", request.Id)).CapXml;
}
