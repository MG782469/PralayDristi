using Hangfire;
using Hangfire.Redis.StackExchange;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Options;
using PralayDrishti.Application.Jobs;
using PralayDrishti.Infrastructure.Caching;
using Npgsql;
using StackExchange.Redis;

namespace PralayDrishti.Infrastructure.BackgroundJobs;

public static class BackgroundJobsSetup
{
    public const string SimulationsQueue = "simulations";

    /// <summary>
    /// Probes Redis once at startup (2 s). Up: Redis cache + Hangfire on Redis. Down: memory cache + in-process
    /// scheduler, and the API still starts - a laptop without Docker running must not be a dead demo.
    /// </summary>
    /// <summary>
    /// Key prefix for everything this deployment puts in Redis (cache and Hangfire). Two apps pointed at the
    /// same Redis but different databases - dev and the integration tests, or two tenants - must not read each
    /// other's cached rows or pick up each other's jobs. Defaults to the database name; Redis:InstanceName overrides.
    /// </summary>
    public static string RedisNamespace(IConfiguration config)
    {
        string? name = config["Redis:InstanceName"];
        if (string.IsNullOrWhiteSpace(name))
        {
            try { name = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection")).Database; }
            catch (ArgumentException) { name = null; }
        }
        return $"pralay:{(string.IsNullOrWhiteSpace(name) ? "default" : name)}:";
    }

    public static IServiceCollection AddCachingAndJobs(this IServiceCollection services, IConfiguration config)
    {
        string ns = RedisNamespace(config);
        services.AddMemoryCache();
        string redisCs = config.GetConnectionString("Redis") ?? "";
        IConnectionMultiplexer? mux = null;
        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            try
            {
                var opts = ConfigurationOptions.Parse(redisCs);
                opts.ConnectTimeout = 2000;
                opts.AbortOnConnectFail = true;
                mux = ConnectionMultiplexer.Connect(opts);
            }
            catch (RedisConnectionException)
            {
                mux = null;
            }
        }
        services.AddSingleton(new RedisStatus(mux != null));

        if (mux != null)
        {
            services.AddSingleton(mux);
            services.AddStackExchangeRedisCache(o =>
            {
                o.ConnectionMultiplexerFactory = () => Task.FromResult(mux);
                o.InstanceName = ns;
            });
            services.AddSingleton<ICacheService>(sp => new ResilientCacheService(
                sp.GetRequiredService<IDistributedCache>(), sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<ILogger<ResilientCacheService>>()));

            var jobOpts = config.GetSection(HangfireJobOptions.Section).Get<HangfireJobOptions>() ?? new HangfireJobOptions();
            services.AddHangfire(h => h
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseRedisStorage(mux, new RedisStorageOptions { Prefix = ns + "hangfire:" }));
            // simulations are CPU heavy: their own queue with a small worker count, everything else on "default"
            services.AddHangfireServer(o =>
            {
                o.ServerName = $"{Environment.MachineName}-sim";
                o.Queues = new[] { SimulationsQueue };
                o.WorkerCount = Math.Max(1, jobOpts.SimulationWorkers);
            });
            services.AddHangfireServer(o =>
            {
                o.ServerName = $"{Environment.MachineName}-default";
                o.Queues = new[] { "default" };
                o.WorkerCount = 2;
            });
            services.AddSingleton<IBackgroundJobScheduler, HangfireJobScheduler>();
        }
        else
        {
            services.AddSingleton<ICacheService>(sp => new ResilientCacheService(null, sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<ILogger<ResilientCacheService>>()));
            var fallbackOpts = config.GetSection(HangfireJobOptions.Section).Get<HangfireJobOptions>() ?? new HangfireJobOptions();
            services.AddSingleton(sp => new InProcessJobScheduler(sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<InProcessJobScheduler>>(), fallbackOpts.SimulationWorkers));
            services.AddSingleton<IBackgroundJobScheduler>(sp => sp.GetRequiredService<InProcessJobScheduler>());
            services.AddHostedService(sp => sp.GetRequiredService<InProcessJobScheduler>());
        }

        services.AddHostedService<RecurringJobsRegistrar>();
        return services;
    }
}

public sealed record RedisStatus(bool Connected);

/// <summary>Registers the Application's recurring jobs with whichever scheduler is active.</summary>
public sealed class RecurringJobsRegistrar : IHostedService
{
    private readonly IBackgroundJobScheduler _scheduler;
    private readonly RedisStatus _redis;
    private readonly ILogger<RecurringJobsRegistrar> _log;

    public RecurringJobsRegistrar(IBackgroundJobScheduler scheduler, RedisStatus redis, ILogger<RecurringJobsRegistrar> log)
    {
        _scheduler = scheduler;
        _redis = redis;
        _log = log;
    }

    public Task StartAsync(CancellationToken ct)
    {
        RecurringJobs.Register(_scheduler);
        if (!_redis.Connected)
            _log.LogWarning("Redis not reachable - using in-memory cache and in-process jobs (jobs will not survive a restart)");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
