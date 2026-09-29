using System.Linq.Expressions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Common.Abstractions;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    UserRole? Role { get; }
    string? IpAddress { get; }
}

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

public interface IPasswordHasher
{
    string Hash(User user, string password);
    bool Verify(User user, string hash, string password);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresUtc) CreateAccessToken(User user);
    (string Raw, string Hash, DateTime ExpiresUtc) CreateRefreshToken();
    string Hash(string rawRefreshToken);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}

public interface IBackgroundJobScheduler
{
    string Enqueue<TJob>(Expression<Func<TJob, Task>> call, string queue = "default");
    bool Delete(string jobId);
    void AddOrUpdateRecurring<TJob>(string id, Expression<Func<TJob, Task>> call, string cron);
}

public interface IRunNotifier
{
    Task ProgressAsync(Guid runId, string stage, double fraction);
    Task StatusAsync(Guid runId, string status);
    Task AlertAsync(AlertDto alert);
    Task LakeAsync(LakeDto lake);
}

public sealed record EngineRunResult(KeyNumbersDto Numbers, IReadOnlyList<string> Log, AlertDraft? Alert);

/// <summary>What the engine thinks an alert for this run should say; the Application decides whether to issue it.</summary>
public sealed record AlertDraft(string Headline, AlertSeverity Severity, int PopulationAtRisk, double? FirstArrivalMin);

public interface IFloodSimulationEngine
{
    Task<EngineRunResult> RunAsync(Guid runId, ScenarioSpec spec, IProgress<(string Stage, double Fraction)> progress, CancellationToken ct);

    /// <summary>Default scenario the dashboard pre-fills for a register dam.</summary>
    ScenarioSpec DefaultScenario(Dam dam, IReadOnlyList<Dam> register, bool cascade);

    /// <summary>CAP 1.2 XML for a finished run.</summary>
    string BuildCap(Guid runId, AlertStatus status, bool probabilistic);

    /// <summary>Natural-dam scenario for a detected lake: estimates level / volume and places the blockage.</summary>
    Task<(ScenarioSpec Spec, double LevelM, double VolumeMcm, string Notes)> ScenarioForLakeAsync(LakeDetection lake, CancellationToken ct);
}

public interface IResultStore
{
    bool Exists(Guid runId);
    Task<JsonPayload?> GetDetailAsync(Guid runId, KeyNumbersDto? numbers, CancellationToken ct = default);
    Task<JsonPayload?> GetProfileAsync(Guid runId, CancellationToken ct = default);
    Task<JsonPayload?> SamplePointAsync(Guid runId, double lon, double lat, CancellationToken ct = default);
    void Delete(Guid runId);
}

public interface IExportService
{
    IReadOnlyList<string> Formats { get; }
    Task<FileResultDto> ExportAsync(Guid runId, string format, CancellationToken ct = default);
}

public interface IMapRenderer
{
    byte[]? RenderLayerPng(Guid runId, string layer);
    Task<(byte[] Body, string ContentType, int Status)> WmsAsync(IReadOnlyDictionary<string, string> query, string baseUrl, CancellationToken ct);
}

public interface IHydraulicAnalysis
{
    Task<JsonPayload> EstimateNaturalDamAsync(List<double[]> polygon, double? crest, double? heightAboveBed, double? currentLevel, DamType type, CancellationToken ct);
    Task<JsonPayload> EstimateReservoirAsync(double lon, double lat, double? areaKm2, double? altimetryLevel, double deadStorageMcm, double maxRise, CancellationToken ct);
    JsonPayload CalculateBreach(DamType type, double heightM, double? waterDepthM, double volumeMcm, FailureMode mode, double crestLengthM, double crestWidthM, double storageExponent);
    Task<JsonPayload> InverseThresholdAsync(Guid runId, double lon, double lat, double depth, string? targetName, CancellationToken ct);
    Task<JsonPayload> InverseFromObservedAsync(Guid runId, string observedGeoJson, CancellationToken ct);
    Task<IReadOnlyList<LakeDetectionInput>> DetectNewLakesAsync(string beforePath, string afterPath, double threshold, double minAreaKm2, CancellationToken ct);
    Task<double> WaterAreaKm2Async(string ndwiPath, double lon, double lat, double threshold, CancellationToken ct);
}

public interface IValidationBenchmarks
{
    JsonPayload Analytic(double h0, double h1, double t);
    Task<JsonPayload> SphCheckAsync(int resolution, double t, CancellationToken ct);
    Task<JsonPayload> ScoreboardAsync(CancellationToken ct);
    Task<JsonPayload> CompareAsync(Guid runId, string observedGeoJson, string? permanentWaterGeoJson, string? arrivalsJson, string? eventName, double depthThreshold, CancellationToken ct);
    Task<JsonPayload> RunBenchmarkAsync(string name, CancellationToken ct);
}

public interface IDemProvider
{
    Task<JsonPayload> ListUploadedAsync(CancellationToken ct);
    Task<string> SaveUploadAsync(Stream content, string fileName, CancellationToken ct);
}

public interface IExposureProvider
{
    Task<string> GetGeoJsonAsync(BoundingBox box, bool includeOsm, CancellationToken ct);
    Task<(int Points, int Roads, int Buildings)> SaveUploadAsync(string geoJson, string fileName, CancellationToken ct);
}

public interface IFileStorage
{
    Task<string> SaveTempAsync(Stream content, string extension, CancellationToken ct);
    void Delete(string path);
    /// <summary>Lake detections dropped by the GEE script (or copied over an air gap); returns and archives them.</summary>
    IReadOnlyList<(string Name, string Json)> TakeOutbox();
    long TerrainCacheBytes();
}
