using MediatR;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Services;

namespace PralayDrishti.Application.Features.Queries.Atlas.GetLeaderboard;

public sealed record GetLeaderboardQuery : IRequest<IReadOnlyList<LeaderboardRowDto>>, ICacheableQuery
{
    public string CacheKey => CacheKeys.Leaderboard;
    public TimeSpan Expiry => TimeSpan.FromMinutes(2);
}
