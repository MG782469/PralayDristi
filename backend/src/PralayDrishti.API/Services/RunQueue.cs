using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PralayDrishti.API.Services;

/// <summary>
/// In-process job queue. A simulation takes seconds to minutes; Celery/Redis would be the Python answer,
/// a bounded channel + hosted service does the same job here with zero extra containers.
/// </summary>
public sealed class RunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public ValueTask EnqueueAsync(Guid id) => _channel.Writer.WriteAsync(id);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    public CancellationTokenSource Track(Guid id, CancellationToken stopping)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _running[id] = cts;
        return cts;
    }

    public void Untrack(Guid id)
    {
        if (_running.TryRemove(id, out var cts)) cts.Dispose();
    }

    public bool Cancel(Guid id)
    {
        if (!_running.TryGetValue(id, out var cts)) return false;
        cts.Cancel();
        return true;
    }

    public int RunningCount => _running.Count;
}
