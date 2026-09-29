using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PralayDrishti.Infrastructure.BackgroundJobs;
using PralayDrishti.Infrastructure.Caching;

namespace PralayDrishti.UnitTests.Infrastructure;

public class CacheFallbackTests
{
    [Fact]
    public async Task Cache_falls_back_to_memory_when_redis_unreachable()
    {
        // nothing listens on port 1; the cache must not throw, it must answer from memory
        var redis = new RedisCache(new RedisCacheOptions
        {
            Configuration = "localhost:1,connectTimeout=200,abortConnect=false,syncTimeout=200"
        });
        var cache = new ResilientCacheService(redis, new MemoryCache(new MemoryCacheOptions()),
            NullLogger<ResilientCacheService>.Instance);

        await cache.SetAsync("k", new[] { 1, 2, 3 }, TimeSpan.FromMinutes(1));
        var back = await cache.GetAsync<int[]>("k");

        Assert.Equal(new[] { 1, 2, 3 }, back);
        Assert.False(cache.RedisHealthy);
    }

    [Fact]
    public async Task Remove_clears_value()
    {
        var cache = new ResilientCacheService(null, new MemoryCache(new MemoryCacheOptions()), NullLogger<ResilientCacheService>.Instance);
        await cache.SetAsync("a", "x", TimeSpan.FromMinutes(1));
        await cache.RemoveAsync("a");
        Assert.Null(await cache.GetAsync<string>("a"));
    }
}

public class InProcessSchedulerTests
{
    public sealed class Recorder
    {
        public readonly TaskCompletionSource<string> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
    }

    public sealed class TestJob
    {
        private readonly Recorder _r;
        public TestJob(Recorder r) => _r = r;

        public async Task Run(string value)
        {
            await Task.Delay(50);
            Interlocked.Increment(ref _r.Calls);
            _r.Done.TrySetResult(value);
        }
    }

    private static (InProcessJobScheduler scheduler, Recorder rec, ServiceProvider sp) Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddTransient<TestJob>();
        var sp = services.BuildServiceProvider();
        var scheduler = new InProcessJobScheduler(sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<InProcessJobScheduler>.Instance);
        return (scheduler, sp.GetRequiredService<Recorder>(), sp);
    }

    [Fact]
    public async Task InProcess_scheduler_runs_enqueued_job()
    {
        var (scheduler, rec, sp) = Build();
        await scheduler.StartAsync(default);
        scheduler.Enqueue<TestJob>(j => j.Run("hello"));
        var result = await rec.Done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("hello", result);
        await scheduler.StopAsync(default);
        await sp.DisposeAsync();
    }

    [Fact]
    public async Task InProcess_delete_cancels_queued_job()
    {
        var (scheduler, rec, sp) = Build();
        // not started yet, so the job is still queued when deleted
        string id = scheduler.Enqueue<TestJob>(j => j.Run("never"));
        Assert.True(scheduler.Delete(id));
        await scheduler.StartAsync(default);
        await Task.Delay(300);
        Assert.Equal(0, rec.Calls);
        await scheduler.StopAsync(default);
        await sp.DisposeAsync();
    }
}
