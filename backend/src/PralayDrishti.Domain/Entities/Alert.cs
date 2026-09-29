using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Domain.Entities;

public class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public SimulationRun? Run { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string Headline { get; set; } = "";
    public AlertSeverity Severity { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Exercise;
    public int PopulationAtRisk { get; set; }
    public double? FirstArrivalMin { get; set; }
    public string CapXml { get; set; } = "";
    public bool Acknowledged { get; set; }
}
