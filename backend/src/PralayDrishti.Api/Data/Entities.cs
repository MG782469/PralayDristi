using System.ComponentModel.DataAnnotations;

namespace PralayDrishti.Api.Data;

public class DamEntity
{
    public int Id { get; set; }
    [MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(80)] public string River { get; set; } = "";
    [MaxLength(80)] public string State { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    [MaxLength(30)] public string Type { get; set; } = "Earthfill";
    public double HeightM { get; set; }
    public double GrossStorageMcm { get; set; }
    public double CrestLengthM { get; set; }
    public double CrestWidthM { get; set; } = 10;
    public double? DownstreamKm { get; set; }
    [MaxLength(40)] public string Category { get; set; } = "Reservoir";   // Reservoir | NaturalLake | Glacial | Transboundary
    public bool Approximate { get; set; } = true;
    [MaxLength(400)] public string Notes { get; set; } = "";
    [MaxLength(200)] public string Source { get; set; } = "";
    public string? CascadeWithJson { get; set; }        // ids of downstream dams used for cascade demo
}

public enum RunStatus { Queued, Running, Completed, Failed, Cancelled }

public class RunEntity
{
    public Guid Id { get; set; }
    [MaxLength(160)] public string Name { get; set; } = "";
    public RunStatus Status { get; set; }
    public double Progress { get; set; }
    [MaxLength(120)] public string Stage { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public string SpecJson { get; set; } = "{}";
    public string? SummaryJson { get; set; }
    public string? Error { get; set; }
    public int? DamId { get; set; }
    [MaxLength(30)] public string Mode { get; set; } = "";
    [MaxLength(30)] public string Origin { get; set; } = "user";   // user | lakewatch | atlas | inverse
    public Guid? LakeDetectionId { get; set; }
}

public class LakeDetectionEntity
{
    public Guid Id { get; set; }
    public DateTime DetectedUtc { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double AreaKm2 { get; set; }
    public double? PreviousAreaKm2 { get; set; }
    public string? PolygonJson { get; set; }            // [[lon,lat],...]
    [MaxLength(60)] public string Source { get; set; } = "GEE Sentinel-2 NDWI";
    [MaxLength(80)] public string Region { get; set; } = "";
    public double? EstimatedVolumeMcm { get; set; }
    public double? EstimatedLevel { get; set; }
    [MaxLength(30)] public string State { get; set; } = "New";      // New | Estimating | Simulating | Alerted | Dismissed | Failed
    public Guid? RunId { get; set; }
    public string? Notes { get; set; }
}

public class AlertEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public DateTime CreatedUtc { get; set; }
    [MaxLength(200)] public string Headline { get; set; } = "";
    [MaxLength(20)] public string Severity { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "Exercise";
    public int PopulationAtRisk { get; set; }
    public double? FirstArrivalMin { get; set; }
    public string CapXml { get; set; } = "";
    public bool Acknowledged { get; set; }
}
