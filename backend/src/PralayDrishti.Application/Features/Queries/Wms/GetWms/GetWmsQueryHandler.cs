using PralayDrishti.Application.Common.Models;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Application.Features.Queries.Wms.GetWms;

public sealed class GetWmsQueryHandler : IRequestHandler<GetWmsQuery, WmsResponse>
{
    private readonly IMapRenderer _renderer;

    public GetWmsQueryHandler(IMapRenderer renderer) => _renderer = renderer;

    public async Task<WmsResponse> Handle(GetWmsQuery request, CancellationToken ct)
    {
        var (body, type, status) = await _renderer.WmsAsync(request.Parameters, request.BaseUrl, ct);
        return new WmsResponse(body, type, status);
    }
}
