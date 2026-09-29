using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Common.Models;

public sealed record UserDto(Guid Id, string Email, string FullName, UserRole Role, bool IsActive, DateTime CreatedUtc, DateTime? LastLoginUtc);

public sealed record AuthResult(
    string AccessToken,
    DateTime AccessTokenExpiresUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresUtc,
    UserDto User);

public sealed record DamDto(
    int Id, string Name, string River, string State, double Lat, double Lon, DamType Type, double HeightM,
    double GrossStorageMcm, double CrestLengthM, double CrestWidthM, double? DownstreamKm, DamCategory Category,
    bool Approximate, string Notes, string Source, List<string> CascadeWith, List<double[]>? Dykes);

public sealed record KeyNumbersDto(
    double FloodedAreaKm2, int PopulationAtRisk, int Settlements, double? FirstArrivalMin, double PeakDischarge,
    double MaxDepth, double LossCrore, int BridgesOvertopped, double? ReachKm, string DemSource, bool SyntheticDem);

public sealed record RunSummaryDto(
    Guid Id, string Name, RunStatus Status, double Progress, string Stage, DateTime CreatedUtc, DateTime? FinishedUtc,
    string Mode, RunOrigin Origin, int? DamId, string? Error, KeyNumbersDto? Numbers);

public sealed record LakeDto(
    Guid Id, DateTime DetectedUtc, double Lat, double Lon, double AreaKm2, double? PreviousAreaKm2, string Source,
    string Region, double? EstimatedVolumeMcm, double? EstimatedLevel, LakeState State, Guid? RunId, string? Notes);

public sealed record AlertDto(
    Guid Id, Guid RunId, DateTime CreatedUtc, string Headline, AlertSeverity Severity, AlertStatus Status,
    int PopulationAtRisk, double? FirstArrivalMin, bool Acknowledged);

public sealed record FileResultDto(byte[] Data, string ContentType, string FileName);

/// <summary>
/// A response the engine renders to JSON itself (run detail, analysis results). The shapes are rich and only
/// displayed, so Application passes them through instead of mirroring every engine record.
/// </summary>
public sealed record JsonPayload(string Json);

public sealed record LakeDetectionInput(
    double Lat, double Lon, double AreaKm2, double? PreviousAreaKm2 = null, List<double[]>? Polygon = null,
    string? Source = null, string? Region = null, DateTime? Date = null);

public sealed record BoundingBox(double West, double South, double East, double North);
