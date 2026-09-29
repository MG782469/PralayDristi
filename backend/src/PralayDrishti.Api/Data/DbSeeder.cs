using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PralayDrishti.Api.Services;

namespace PralayDrishti.Api.Data;

public static class DbSeeder
{
    private sealed class SeedDam
    {
        public string Name { get; set; } = "";
        public string River { get; set; } = "";
        public string State { get; set; } = "";
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string Type { get; set; } = "Earthfill";
        public double HeightM { get; set; }
        public double GrossStorageMcm { get; set; }
        public double CrestLengthM { get; set; }
        public double CrestWidthM { get; set; } = 10;
        public double? DownstreamKm { get; set; }
        public string Category { get; set; } = "Reservoir";
        public bool Approximate { get; set; } = true;
        public string Notes { get; set; } = "";
        public string Source { get; set; } = "";
        public List<string> CascadeWith { get; set; } = new();
    }

    public static async Task SeedAsync(AppDbContext db, DataPaths paths, ILogger log)
    {
        await db.Database.EnsureCreatedAsync();

        // runs that were in flight when the process died will never finish
        var stale = await db.Runs.Where(r => r.Status == RunStatus.Running || r.Status == RunStatus.Queued).ToListAsync();
        foreach (var r in stale)
        {
            r.Status = RunStatus.Failed;
            r.Error = "Service restarted while the run was in progress.";
        }
        if (stale.Count > 0) await db.SaveChangesAsync();

        if (await db.Dams.AnyAsync() || !File.Exists(paths.SeedDams)) return;
        var seed = JsonSerializer.Deserialize<List<SeedDam>>(await File.ReadAllTextAsync(paths.SeedDams),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        foreach (var s in seed)
        {
            db.Dams.Add(new DamEntity
            {
                Name = s.Name, River = s.River, State = s.State, Lat = s.Lat, Lon = s.Lon, Type = s.Type,
                HeightM = s.HeightM, GrossStorageMcm = s.GrossStorageMcm, CrestLengthM = s.CrestLengthM,
                CrestWidthM = s.CrestWidthM, DownstreamKm = s.DownstreamKm, Category = s.Category,
                Approximate = s.Approximate, Notes = s.Notes, Source = s.Source,
                CascadeWithJson = s.CascadeWith.Count > 0 ? JsonSerializer.Serialize(s.CascadeWith) : null
            });
        }
        await db.SaveChangesAsync();
        log.LogInformation("Seeded {Count} dams", seed.Count);
    }
}
