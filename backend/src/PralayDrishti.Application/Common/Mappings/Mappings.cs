using System.Text.Json;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Entities;

namespace PralayDrishti.Application.Common.Mappings;

public static class Mappings
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static UserDto ToDto(this User u) => new(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedUtc, u.LastLoginUtc);

    public static DamDto ToDto(this Dam d) => new(d.Id, d.Name, d.River, d.State, d.Lat, d.Lon, d.Type, d.HeightM,
        d.GrossStorageMcm, d.CrestLengthM, d.CrestWidthM, d.DownstreamKm, d.Category, d.Approximate, d.Notes, d.Source,
        d.CascadeWith, d.Dykes);

    public static void Apply(this Dam d, DamDto x)
    {
        d.Name = x.Name.Trim(); d.River = x.River.Trim(); d.State = x.State.Trim(); d.Lat = x.Lat; d.Lon = x.Lon; d.Type = x.Type;
        d.HeightM = x.HeightM; d.GrossStorageMcm = x.GrossStorageMcm; d.CrestLengthM = x.CrestLengthM; d.CrestWidthM = x.CrestWidthM;
        d.DownstreamKm = x.DownstreamKm; d.Category = x.Category; d.Approximate = x.Approximate; d.Notes = x.Notes ?? "";
        d.Source = x.Source ?? ""; d.CascadeWith = x.CascadeWith ?? new(); d.Dykes = x.Dykes;
    }

    public static RunSummaryDto ToDto(this SimulationRun r) => new(r.Id, r.Name, r.Status, Math.Round(r.Progress, 3), r.Stage,
        r.CreatedUtc, r.FinishedUtc, r.Mode, r.Origin, r.DamId, r.Error,
        string.IsNullOrEmpty(r.SummaryJson) ? null : JsonSerializer.Deserialize<KeyNumbersDto>(r.SummaryJson, Json));

    public static LakeDto ToDto(this LakeDetection l) => new(l.Id, l.DetectedUtc, l.Lat, l.Lon, l.AreaKm2, l.PreviousAreaKm2,
        l.Source, l.Region, l.EstimatedVolumeMcm, l.EstimatedLevel, l.State, l.RunId, l.Notes);

    public static AlertDto ToDto(this Alert a) => new(a.Id, a.RunId, a.CreatedUtc, a.Headline, a.Severity, a.Status,
        a.PopulationAtRisk, a.FirstArrivalMin, a.Acknowledged);
}
