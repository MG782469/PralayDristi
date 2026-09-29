using MediatR;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Application.Features.Queries.Data.GetTerrainTile;

public sealed class GetTerrainTileQueryHandler : IRequestHandler<GetTerrainTileQuery, byte[]?>
{
    private readonly IDemProvider _dem;

    public GetTerrainTileQueryHandler(IDemProvider dem) => _dem = dem;

    public Task<byte[]?> Handle(GetTerrainTileQuery request, CancellationToken ct) => _dem.GetTerrainTileAsync(request.Z, request.X, request.Y, ct);
}
