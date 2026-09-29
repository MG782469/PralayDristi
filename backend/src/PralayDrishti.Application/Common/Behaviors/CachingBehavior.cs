using MediatR;
using Microsoft.Extensions.Logging;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;

namespace PralayDrishti.Application.Common.Behaviors;

public sealed class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICacheService _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _log;

    public CachingBehavior(ICacheService cache, ILogger<CachingBehavior<TRequest, TResponse>> log)
    {
        _cache = cache;
        _log = log;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is ICacheableQuery q && !q.BypassCache)
        {
            var hit = await _cache.GetAsync<TResponse>(q.CacheKey, ct);
            if (hit is not null)
            {
                _log.LogDebug("cache hit {Key}", q.CacheKey);
                return hit;
            }
            var fresh = await next();
            if (fresh is not null) await _cache.SetAsync(q.CacheKey, fresh, q.Expiry, ct);
            return fresh;
        }

        var response = await next();
        if (request is ICacheInvalidator inv)
            foreach (var key in inv.InvalidatesKeys)
                await _cache.RemoveAsync(key, ct);
        return response;
    }
}
