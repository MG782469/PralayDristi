using MediatR;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Services;

namespace PralayDrishti.Application.Features.Queries.Atlas.GetLeaderboard;

public sealed class GetLeaderboardQueryHandler : IRequestHandler<GetLeaderboardQuery, IReadOnlyList<LeaderboardRowDto>>
{
    private readonly AtlasService _atlas;

    public GetLeaderboardQueryHandler(AtlasService atlas) => _atlas = atlas;

    public Task<IReadOnlyList<LeaderboardRowDto>> Handle(GetLeaderboardQuery request, CancellationToken ct) => _atlas.LeaderboardAsync(ct);
}
