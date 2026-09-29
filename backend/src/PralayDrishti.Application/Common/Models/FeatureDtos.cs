namespace PralayDrishti.Application.Common.Models;

public sealed record UploadExposureResult(int Points, int Roads, int Buildings);

public sealed record WmsResponse(byte[] Body, string ContentType, int Status);

public sealed record SystemInfoDto(
    string Name, string? Version, bool Offline, bool Osm, bool Delft3d, bool Redis, int RunningRuns, int Dams, int Runs, int Alerts,
    double TerrainCacheMb, IReadOnlyList<string> ExportFormats, string Machine, int Cores);
