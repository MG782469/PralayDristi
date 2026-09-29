using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Domain.Entities;

/// <summary>A new or growing water body found by the GEE lake watch (or an offline NDWI comparison).</summary>
public class LakeDetection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime DetectedUtc { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double AreaKm2 { get; set; }
    public double? PreviousAreaKm2 { get; set; }
    public List<double[]>? Polygon { get; set; }
    public string Source { get; set; } = "GEE Sentinel-2 NDWI";
    public string Region { get; set; } = "";
    public double? EstimatedVolumeMcm { get; set; }
    public double? EstimatedLevel { get; set; }
    public LakeState State { get; set; } = LakeState.New;
    public Guid? RunId { get; set; }
    public SimulationRun? Run { get; set; }
    public string? Notes { get; set; }
}
