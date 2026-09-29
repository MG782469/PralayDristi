using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Infrastructure.Services;

public sealed class FileStorage : IFileStorage
{
    private readonly DataPaths _paths;

    public FileStorage(DataPaths paths) => _paths = paths;

    public async Task<string> SaveTempAsync(Stream content, string extension, CancellationToken ct)
    {
        string path = Path.Combine(_paths.Uploads, Guid.NewGuid().ToString("N") + extension);
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
        return path;
    }

    public void Delete(string path)
    {
        // only ever delete inside our own upload folder
        if (Path.GetFullPath(path).StartsWith(Path.GetFullPath(_paths.Uploads), StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            File.Delete(path);
    }

    public IReadOnlyList<(string Name, string Json)> TakeOutbox()
    {
        var list = new List<(string, string)>();
        foreach (var file in Directory.GetFiles(_paths.GeeOutbox, "*.json"))
        {
            string json = File.ReadAllText(file);
            File.Move(file, Path.Combine(_paths.GeeProcessed, $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Path.GetFileName(file)}"), true);
            list.Add((Path.GetFileName(file), json));
        }
        return list;
    }

    public long TerrainCacheBytes() => Directory.Exists(_paths.Tiles)
        ? new DirectoryInfo(_paths.Tiles).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
        : 0;
}
