using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Infrastructure.Caching;

/// <summary>
/// Redis when it is there, process memory when it is not. A dead Redis must never turn a read into a 500 -
/// after a failure we stop trying Redis for 30 s and serve from memory.
/// </summary>
public sealed class ResilientCacheService : ICacheService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly IDistributedCache? _redis;
    private readonly IMemoryCache _memory;
    private readonly ILogger<ResilientCacheService> _log;
    private DateTime _redisDownUntil = DateTime.MinValue;

    public ResilientCacheService(IDistributedCache? redis, IMemoryCache memory, ILogger<ResilientCacheService> log)
    {
        _redis = redis;
        _memory = memory;
        _log = log;
    }

    public bool RedisHealthy => _redis != null && DateTime.UtcNow >= _redisDownUntil;

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (RedisHealthy)
        {
            try
            {
                var bytes = await _redis!.GetAsync(key, ct);
                return bytes == null ? default : JsonSerializer.Deserialize<T>(bytes, Json);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MarkDown(ex);
            }
        }
        return _memory.TryGetValue(key, out byte[]? local) && local != null ? JsonSerializer.Deserialize<T>(local, Json) : default;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken ct = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        // always keep a local copy too: cheap, and it's what we serve if Redis drops mid-flight
        _memory.Set(key, bytes, expiry);
        if (!RedisHealthy) return;
        try
        {
            await _redis!.SetAsync(key, bytes, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiry }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MarkDown(ex);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _memory.Remove(key);
        if (!RedisHealthy) return;
        try
        {
            await _redis!.RemoveAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MarkDown(ex);
        }
    }

    private void MarkDown(Exception ex)
    {
        if (DateTime.UtcNow >= _redisDownUntil)
            _log.LogWarning("Redis unavailable ({Msg}); using in-memory cache for {Sec} s", ex.Message, RetryAfter.TotalSeconds);
        _redisDownUntil = DateTime.UtcNow + RetryAfter;
    }
}
