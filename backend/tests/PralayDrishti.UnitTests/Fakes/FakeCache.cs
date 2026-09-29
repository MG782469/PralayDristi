using System.Collections.Concurrent;
using System.Text.Json;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.UnitTests.Fakes;

public sealed class FakeCache : ICacheService
{
    private readonly ConcurrentDictionary<string, string> _data = new();
    public IEnumerable<string> Keys => _data.Keys;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
        Task.FromResult(_data.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : default);

    public Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken ct = default)
    {
        _data[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _data.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
