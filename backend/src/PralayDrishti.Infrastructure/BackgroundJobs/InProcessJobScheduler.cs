using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Infrastructure.BackgroundJobs;

/// <summary>
/// Fallback scheduler when Redis (and therefore Hangfire) is not available: a channel drained by a hosted
/// service - the same thing the prototype used - plus a minute timer for recurring jobs.
/// Jobs do not survive a restart here; that is what Hangfire is for.
/// </summary>
public sealed class InProcessJobScheduler : BackgroundService, IBackgroundJobScheduler
{
    private readonly Channel<(string Id, string Queue, Func<IServiceProvider, Task> Run)> _queue =
        Channel.CreateUnbounded<(string, string, Func<IServiceProvider, Task>)>();
    private readonly ConcurrentDictionary<string, byte> _deleted = new();
    private readonly ConcurrentDictionary<string, (Cron Cron, Func<IServiceProvider, Task> Run)> _recurring = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<InProcessJobScheduler> _log;
    // same cap Hangfire gets via SimulationWorkers: simulations are CPU and memory heavy
    private readonly SemaphoreSlim _simulationGate;

    public InProcessJobScheduler(IServiceScopeFactory scopes, ILogger<InProcessJobScheduler> log, int simulationWorkers = 1)
    {
        _scopes = scopes;
        _log = log;
        _simulationGate = new SemaphoreSlim(Math.Max(1, simulationWorkers));
    }

    public string Enqueue<TJob>(Expression<Func<TJob, Task>> call, string queue = "default")
    {
        string id = "inproc-" + Guid.NewGuid().ToString("N")[..12];
        _queue.Writer.TryWrite((id, queue, Bind(call)));
        return id;
    }

    public bool Delete(string jobId) => _deleted.TryAdd(jobId, 0);

    public void AddOrUpdateRecurring<TJob>(string id, Expression<Func<TJob, Task>> call, string cron) =>
        _recurring[id] = (Cron.Parse(cron), Bind(call));

    private static Func<IServiceProvider, Task> Bind<TJob>(Expression<Func<TJob, Task>> call)
    {
        var compiled = call.Compile();
        return sp => compiled(sp.GetRequiredService<TJob>());
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(DrainAsync(stoppingToken), TickAsync(stoppingToken));

    private async Task DrainAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var (id, queue, run) in _queue.Reader.ReadAllAsync(ct))
            {
                if (_deleted.TryRemove(id, out _)) continue;
                // fire and forget: a slow simulation must not hold up small jobs on the default queue
                _ = queue == BackgroundJobsSetup.SimulationsQueue
                    ? Task.Run(() => RunGatedAsync(id, run, ct), ct)
                    : Task.Run(() => RunSafely(id, run), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var now = DateTime.UtcNow;
                foreach (var (id, job) in _recurring)
                    if (job.Cron.Matches(now)) _ = Task.Run(() => RunSafely(id, job.Run), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunGatedAsync(string id, Func<IServiceProvider, Task> run, CancellationToken ct)
    {
        await _simulationGate.WaitAsync(ct);
        try
        {
            // deleted while waiting for a worker slot
            if (_deleted.TryRemove(id, out _)) return;
            await RunSafely(id, run);
        }
        finally
        {
            _simulationGate.Release();
        }
    }

    private async Task RunSafely(string id, Func<IServiceProvider, Task> run)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            await run(scope.ServiceProvider);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "In-process job {Id} failed", id);
        }
    }

    /// <summary>Just enough cron for our schedules: "*" or a number in each of the five fields.</summary>
    internal readonly record struct Cron(int? Minute, int? Hour, int? Day, int? Month, int? DayOfWeek)
    {
        public static Cron Parse(string expr)
        {
            var f = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length != 5) throw new FormatException($"bad cron '{expr}'");
            int? P(string s) => s == "*" ? null : int.Parse(s);
            return new Cron(P(f[0]), P(f[1]), P(f[2]), P(f[3]), P(f[4]));
        }

        public bool Matches(DateTime t) =>
            (Minute is null || Minute == t.Minute) && (Hour is null || Hour == t.Hour) && (Day is null || Day == t.Day) &&
            (Month is null || Month == t.Month) && (DayOfWeek is null || DayOfWeek == (int)t.DayOfWeek);
    }
}
