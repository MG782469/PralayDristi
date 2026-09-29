using System.Text;
using MediatR;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Alerts.GetAlertFeed;

public sealed class GetAlertFeedQueryHandler : IRequestHandler<GetAlertFeedQuery, string>
{
    private readonly IAlertRepository _alerts;

    public GetAlertFeedQueryHandler(IAlertRepository alerts) => _alerts = alerts;

    public async Task<string> Handle(GetAlertFeedQuery request, CancellationToken ct)
    {
        var alerts = await _alerts.ListAsync(50, ct);
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<feed xmlns=\"http://www.w3.org/2005/Atom\">");
        sb.AppendLine("<title>PralayDrishti CAP alerts</title>");
        sb.AppendLine($"<id>{request.BaseUrl}/api/alerts/feed</id>");
        sb.AppendLine($"<updated>{(alerts.FirstOrDefault()?.CreatedUtc ?? DateTime.UtcNow):yyyy-MM-ddTHH:mm:ssZ}</updated>");
        foreach (var a in alerts)
        {
            sb.AppendLine("<entry>");
            sb.AppendLine($"<id>urn:uuid:{a.Id}</id>");
            sb.AppendLine($"<title>{System.Security.SecurityElement.Escape(a.Headline)}</title>");
            sb.AppendLine($"<updated>{a.CreatedUtc:yyyy-MM-ddTHH:mm:ssZ}</updated>");
            sb.AppendLine($"<link rel=\"alternate\" type=\"application/cap+xml\" href=\"{request.BaseUrl}/api/alerts/{a.Id}/cap\"/>");
            sb.AppendLine("</entry>");
        }
        sb.AppendLine("</feed>");
        return sb.ToString();
    }
}
