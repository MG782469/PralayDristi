using PralayDrishti.Infrastructure.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Infrastructure.Persistence.Seed;
using PralayDrishti.Infrastructure.Services;

namespace PralayDrishti.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>Applies migrations (when Database:MigrateOnStartup), seeds dams + demo users, fails orphaned runs.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, IConfiguration config, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");

        if (config.GetValue("Database:MigrateOnStartup", true))
            await db.Database.MigrateAsync(ct);

        var paths = sp.GetRequiredService<DataPaths>();
        await DbSeeder.SeedAsync(db, sp.GetRequiredService<IPasswordHasher>(), sp.GetRequiredService<IOptions<SeedOptions>>().Value,
            paths.SeedDams, DateTime.UtcNow, log, ct);
        bool durableJobs = sp.GetRequiredService<RedisStatus>().Connected;
        int orphaned = await DbSeeder.FailOrphanedRunsAsync(db, DateTime.UtcNow, durableJobs, ct);
        if (orphaned > 0) log.LogWarning("Marked {Count} runs left queued/running by the last shutdown as failed", orphaned);
        log.LogInformation("Database ready, data folder {Dir}", paths.Root);
    }
}
