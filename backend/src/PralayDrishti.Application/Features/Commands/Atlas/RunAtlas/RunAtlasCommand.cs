using MediatR;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Services;

namespace PralayDrishti.Application.Features.Commands.Atlas.RunAtlas;

public sealed record RunAtlasCommand(IReadOnlyList<int>? DamIds) : IRequest<IReadOnlyList<Guid>>, ICacheInvalidator
{
    public IEnumerable<string> InvalidatesKeys => new[] { CacheKeys.Leaderboard };
}
