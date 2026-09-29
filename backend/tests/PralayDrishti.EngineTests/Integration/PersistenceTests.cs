using Microsoft.EntityFrameworkCore;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Infrastructure.Persistence;
using PralayDrishti.Infrastructure.Persistence.Seed;

namespace PralayDrishti.EngineTests.Integration;

public class PersistenceTests
{
    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(User user, string password) => "h:" + password;
        public bool Verify(User user, string hash, string password) => hash == "h:" + password;
    }

    private static AppDbContext Fresh()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgresFactAttribute.ConnectionString).UseSnakeCaseNamingConvention().Options);
        db.Database.EnsureDeleted();
        db.Database.Migrate();
        return db;
    }

    private static string SeedFile() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../data/seed/dams.json"));

    [PostgresFact]
    public async Task Seeder_creates_dams_and_demo_users_once()
    {
        await using var db = Fresh();
        var opts = new SeedOptions { DemoUsers = { new() { Email = "a@x.local", Password = "Demo@12345", Role = UserRole.Admin } } };
        await DbSeeder.SeedAsync(db, new PlainHasher(), opts, SeedFile(), DateTime.UtcNow);
        int dams = await db.Dams.CountAsync(), users = await db.Users.CountAsync();
        await DbSeeder.SeedAsync(db, new PlainHasher(), opts, SeedFile(), DateTime.UtcNow);
        Assert.True(dams >= 10);
        Assert.Equal(1, users);
        Assert.Equal(dams, await db.Dams.CountAsync());
        Assert.Equal(users, await db.Users.CountAsync());
    }

    [PostgresFact]
    public async Task Deleting_dam_nulls_run_reference()
    {
        await using var db = Fresh();
        var dam = new Dam { Name = "T", River = "R", State = "S", HeightM = 10, GrossStorageMcm = 1 };
        db.Dams.Add(dam);
        await db.SaveChangesAsync();
        var run = SimulationRun.Queue("r", "DamBreak", "{}", RunOrigin.User, DateTime.UtcNow, dam.Id);
        db.Runs.Add(run);
        await db.SaveChangesAsync();

        db.Dams.Remove(dam);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var back = await db.Runs.SingleAsync(r => r.Id == run.Id);
        Assert.Null(back.DamId);
    }

    private static async Task<(SimulationRun queued, SimulationRun running)> TwoInFlightRuns(AppDbContext db)
    {
        var queued = SimulationRun.Queue("q", "DamBreak", "{}", RunOrigin.User, DateTime.UtcNow);
        var running = SimulationRun.Queue("r", "DamBreak", "{}", RunOrigin.User, DateTime.UtcNow);
        running.Start(DateTime.UtcNow);
        db.Runs.AddRange(queued, running);
        await db.SaveChangesAsync();
        return (queued, running);
    }

    [PostgresFact]
    public async Task In_process_restart_fails_queued_and_running_runs()
    {
        // the in-process channel is gone after a restart: nothing will ever pick these up
        await using var db = Fresh();
        var (queued, running) = await TwoInFlightRuns(db);

        int n = await DbSeeder.FailOrphanedRunsAsync(db, DateTime.UtcNow, jobsAreDurable: false);

        Assert.Equal(2, n);
        Assert.Equal(RunStatus.Failed, queued.Status);
        Assert.Equal(RunStatus.Failed, running.Status);
    }

    [PostgresFact]
    public async Task Hangfire_restart_leaves_runs_for_hangfire_to_resume()
    {
        await using var db = Fresh();
        var (queued, running) = await TwoInFlightRuns(db);

        int n = await DbSeeder.FailOrphanedRunsAsync(db, DateTime.UtcNow, jobsAreDurable: true);

        Assert.Equal(0, n);
        Assert.Equal(RunStatus.Queued, queued.Status);
        Assert.Equal(RunStatus.Running, running.Status);
    }
}
