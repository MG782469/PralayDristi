using Microsoft.Extensions.Configuration;
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
        public int Active, MaxActive, Finished;
    }

    public sealed class SlowJob
    {
        private readonly Recorder _r;
        public SlowJob(Recorder r) => _r = r;

        public async Task Run()
        {
            int now = Interlocked.Increment(ref _r.Active);
            lock (_r) _r.MaxActive = Math.Max(_r.MaxActive, now);
            await Task.Delay(120);
            Interlocked.Decrement(ref _r.Active);
            if (Interlocked.Increment(ref _r.Finished) == 3) _r.Done.TrySetResult("all");
        }
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
        services.AddTransient<SlowJob>();
        var sp = services.BuildServiceProvider();
        var scheduler = new InProcessJobScheduler(sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<InProcessJobScheduler>.Instance, simulationWorkers: 1);
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
    public async Task InProcess_simulations_queue_respects_worker_count()
    {
        // Redis down + "run atlas" queues a simulation per dam; they must not all run at once
        var (scheduler, rec, sp) = Build();
        await scheduler.StartAsync(default);
        for (int i = 0; i < 3; i++) scheduler.Enqueue<SlowJob>(j => j.Run(), "simulations");

        await rec.Done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, rec.MaxActive);
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

public class RedisNamespaceTests
{
    private static IConfiguration Config(params (string, string?)[] kv) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(kv.Select(p => new KeyValuePair<string, string?>(p.Item1, p.Item2))).Build();

    [Fact]
    public void Redis_namespace_follows_the_database_so_two_environments_never_share_keys()
    {
        var dev = BackgroundJobsSetup.RedisNamespace(Config(("ConnectionStrings:DefaultConnection", "Host=localhost;Database=pralaydrishti;Username=u")));
        var test = BackgroundJobsSetup.RedisNamespace(Config(("ConnectionStrings:DefaultConnection", "Host=localhost;Database=pralaydrishti_api_test;Username=u")));

        Assert.Equal("pralay:pralaydrishti:", dev);
        Assert.NotEqual(dev, test);
    }

    [Fact]
    public void Explicit_redis_instance_name_wins()
    {
        var ns = BackgroundJobsSetup.RedisNamespace(Config(
            ("ConnectionStrings:DefaultConnection", "Host=h;Database=db"), ("Redis:InstanceName", "prod-a")));
        Assert.Equal("pralay:prod-a:", ns);
    }
}

public class RedisHealthCheckTests
{
    private static readonly Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext Ctx = new();

    [Fact]
    public async Task Redis_down_at_startup_reports_degraded_not_unhealthy()
    {
        var cache = new ResilientCacheService(null, new MemoryCache(new MemoryCacheOptions()), NullLogger<ResilientCacheService>.Instance);
        var check = new RedisHealthCheck(new RedisStatus(false), cache);

        var r = await check.CheckHealthAsync(Ctx);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, r.Status);
        Assert.Contains("in-process", r.Description);
    }

    [Fact]
    public async Task Redis_dropping_mid_flight_reports_degraded()
    {
        var redis = new RedisCache(new RedisCacheOptions { Configuration = "localhost:1,connectTimeout=200,abortConnect=false,syncTimeout=200" });
        var cache = new ResilientCacheService(redis, new MemoryCache(new MemoryCacheOptions()), NullLogger<ResilientCacheService>.Instance);
        await cache.SetAsync("k", 1, TimeSpan.FromMinutes(1));   // fails against port 1 and marks Redis down

        var r = await new RedisHealthCheck(new RedisStatus(true), cache).CheckHealthAsync(Ctx);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, r.Status);
    }
}
