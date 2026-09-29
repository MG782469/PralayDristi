using MediatR;

namespace PralayDrishti.Application.Features.Queries.Data.GetTerrainTile;

public sealed record GetTerrainTileQuery(int Z, int X, int Y) : IRequest<byte[]?>;
