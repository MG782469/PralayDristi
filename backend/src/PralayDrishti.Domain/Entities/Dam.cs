using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Domain.Entities;

/// <summary>A dam or natural lake from the register (CWC NRLD / India-WRIS / user added).</summary>
public class Dam
{
    public int Id { get; set; }
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
    public DamCategory Category { get; set; } = DamCategory.Reservoir;
    public bool Approximate { get; set; } = true;
    public string Notes { get; set; } = "";
    public string Source { get; set; } = "";
    public List<string> CascadeWith { get; set; } = new();
    public List<double[]>? Dykes { get; set; }
}
