namespace PralayDrishti.Infrastructure.Services;

public sealed class AppOptions
{
    public string DataDir { get; set; } = "data";
    public bool Offline { get; set; }
    public bool UseOsm { get; set; } = true;
    public string OverpassUrl { get; set; } = "https://overpass-api.de/api/interpreter";
    public int DefaultDemZoom { get; set; } = 12;
    public int MaxConcurrentRuns { get; set; } = 1;
    public string[] CorsOrigins { get; set; } = Array.Empty<string>();
    public string Delft3DCommand { get; set; } = "";
    public LakeWatchOptions LakeWatch { get; set; } = new();
}

public sealed class LakeWatchOptions
{
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 60;
    public bool AutoSimulate { get; set; } = true;
}

/// <summary>Resolved folder layout under DataDir.</summary>
public sealed class DataPaths
{
    public DataPaths(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        foreach (var d in new[] { Dem, Tiles, Runs, Uploads, Osm, GeeOutbox, GeeProcessed, Exposure, Benchmarks, Models })
            Directory.CreateDirectory(d);
    }

    public string Root { get; }
    public string Dem => Path.Combine(Root, "dem");
    public string Tiles => Path.Combine(Root, "cache", "terrain");
    public string Osm => Path.Combine(Root, "cache", "osm");
    public string Runs => Path.Combine(Root, "runs");
    public string Uploads => Path.Combine(Root, "uploads");
    public string GeeOutbox => Path.Combine(Root, "gee_outbox");
    public string GeeProcessed => Path.Combine(Root, "gee_outbox", "processed");
    public string Exposure => Path.Combine(Root, "exposure");
    public string Benchmarks => Path.Combine(Root, "benchmarks");
    public string Models => Path.Combine(Root, "models");
    public string Database => Path.Combine(Root, "pralaydrishti.db");
    public string SeedDams => Path.Combine(Root, "seed", "dams.json");

    public string RunDir(Guid id) => Path.Combine(Runs, id.ToString("N"));
}
