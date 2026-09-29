using Microsoft.Extensions.Diagnostics.HealthChecks;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Infrastructure.Caching;
using StackExchange.Redis;

namespace PralayDrishti.Infrastructure.BackgroundJobs;

/// <summary>
/// Redis backs both the cache and Hangfire. Losing it is survivable (memory cache, in-process jobs), so it
/// reports Degraded rather than Unhealthy: the pod stays in rotation, but monitoring sees jobs are not durable.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly RedisStatus _status;
    private readonly ICacheService _cache;
    private readonly IConnectionMultiplexer? _mux;

    public RedisHealthCheck(RedisStatus status, ICacheService cache, IConnectionMultiplexer? mux = null)
    {
        _status = status;
        _cache = cache;
        _mux = mux;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!_status.Connected)
            return HealthCheckResult.Degraded("Redis unreachable at startup: in-memory cache and in-process jobs (lost on restart)");
        if (_cache is ResilientCacheService { RedisHealthy: false })
            return HealthCheckResult.Degraded("Redis failing: cache served from memory, Hangfire jobs may stall");
        if (_mux == null) return HealthCheckResult.Healthy("Redis cache and Hangfire");
        try
        {
            var ping = await _mux.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis cache and Hangfire ({ping.TotalMilliseconds:F0} ms)");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return HealthCheckResult.Degraded("Redis ping failed: " + ex.Message);
        }
    }
}
