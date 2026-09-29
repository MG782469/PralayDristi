using System.Text.Json;
using PralayDrishti.API.Data;
using PralayDrishti.Infrastructure.Engine.Hydraulics;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.API.Services;

public static class ScenarioFactory
{
    public static DamType ParseType(string t) => Enum.TryParse<DamType>(t, true, out var v) ? v : DamType.Earthfill;

    public static DamSpec ToDamSpec(DamEntity d) => new()
    {
        Name = d.Name,
        Lon = d.Lon,
        Lat = d.Lat,
        Type = ParseType(d.Type),
        HeightM = d.HeightM,
        ReservoirVolumeM3 = d.GrossStorageMcm * 1e6,
        CrestLengthM = d.CrestLengthM > 0 ? d.CrestLengthM : 300,
        CrestWidthM = d.CrestWidthM > 0 ? d.CrestWidthM : 10
    };

    /// <summary>Sensible default scenario for a dam from the register (what the "Simulate" button does).</summary>
    public static ScenarioSpec Default(DamEntity d, IEnumerable<DamEntity> all, bool cascade = false)
    {
        var spec = new ScenarioSpec
        {
            Name = $"{d.Name} failure",
            Mode = ScenarioMode.DamBreak,
            Dam = ToDamSpec(d),
            DownstreamKm = d.DownstreamKm ?? 40,
            // big dams: longer duration and a coarser grid so it stays interactive
            DurationHours = d.GrossStorageMcm > 1000 ? 8 : d.GrossStorageMcm > 100 ? 6 : 4,
            MaxCells = 250_000
        };
        if (cascade && !string.IsNullOrEmpty(d.CascadeWithJson))
        {
            var names = JsonSerializer.Deserialize<List<string>>(d.CascadeWithJson) ?? new();
            var downstream = all.Where(x => names.Contains(x.Name)).ToList();
            if (downstream.Count > 0)
            {
                spec.Mode = ScenarioMode.Cascade;
                spec.DownstreamDams = downstream.Select(ToDamSpec).ToList();
                spec.Name = $"{d.Name} cascade ({string.Join(", ", downstream.Select(x => x.Name))})";
            }
        }
        return spec;
    }

    public static RunEntity NewRun(ScenarioSpec spec, string origin, int? damId = null, Guid? lakeId = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = spec.Name,
        Status = RunStatus.Queued,
        Stage = "Queued",
        CreatedUtc = DateTime.UtcNow,
        SpecJson = JsonSerializer.Serialize(spec, RunArchive.Json),
        Mode = spec.Mode.ToString(),
        Origin = origin,
        DamId = damId,
        LakeDetectionId = lakeId
    };
}
