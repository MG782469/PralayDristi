using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public List<DemoUser> DemoUsers { get; set; } = new();

    public sealed class DemoUser
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public UserRole Role { get; set; } = UserRole.Viewer;
        public string FullName { get; set; } = "";
    }
}

public static class DbSeeder
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, SeedOptions options, string seedDamsPath,
        DateTime nowUtc, ILogger? log = null, CancellationToken ct = default)
    {
        if (!await db.Dams.AnyAsync(ct) && File.Exists(seedDamsPath))
        {
            var dams = JsonSerializer.Deserialize<List<Dam>>(await File.ReadAllTextAsync(seedDamsPath, ct), Json) ?? new();
            foreach (var d in dams) d.Id = 0;
            db.Dams.AddRange(dams);
            log?.LogInformation("Seeded {Count} dams", dams.Count);
        }

        foreach (var demo in options.DemoUsers)
        {
            // an empty password means "not configured here" - never create an account with a blank password
            if (string.IsNullOrWhiteSpace(demo.Email) || string.IsNullOrWhiteSpace(demo.Password)) continue;
            string normalized = User.Normalize(demo.Email);
            if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct)) continue;
            var user = User.Create(demo.Email,
                string.IsNullOrWhiteSpace(demo.FullName) ? $"Demo {demo.Role}" : demo.FullName, "", demo.Role, nowUtc);
            user.SetPasswordHash(hasher.Hash(user, demo.Password));
            db.Users.Add(user);
            log?.LogInformation("Seeded demo user {Email} ({Role})", demo.Email, demo.Role);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Runs left Queued/Running by a crash or restart can never finish; mark them failed.</summary>
    public static async Task<int> FailOrphanedRunsAsync(AppDbContext db, DateTime nowUtc, CancellationToken ct = default)
    {
        var stale = await db.Runs.Where(r => r.Status == RunStatus.Running).ToListAsync(ct);
        foreach (var r in stale) r.Fail("Service restarted while the run was in progress.", nowUtc);
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }
}
