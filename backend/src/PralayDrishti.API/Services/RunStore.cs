using System.IO.Compression;
using System.Text;
using PralayDrishti.Infrastructure.Engine.Export;
using PralayDrishti.Infrastructure.Engine.External;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.API.Services;

/// <summary>Finished runs: small LRU in memory, everything else on disk. Also produces and caches exports.</summary>
public sealed class RunStore
{
    private readonly DataPaths _paths;
    private readonly object _gate = new();
    private readonly LinkedList<(Guid id, ScenarioOutcome o)> _lru = new();
    private const int Capacity = 4;

    public RunStore(DataPaths paths) => _paths = paths;

    public void Put(Guid id, ScenarioOutcome o)
    {
        RunArchive.Save(o, _paths.RunDir(id));
        Remember(id, o);
    }

    public ScenarioOutcome? Get(Guid id)
    {
        lock (_gate)
        {
            var node = _lru.First;
            while (node != null)
            {
                if (node.Value.id == id)
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                    return node.Value.o;
                }
                node = node.Next;
            }
        }
        string dir = _paths.RunDir(id);
        if (!File.Exists(Path.Combine(dir, "meta.json"))) return null;
        var o = RunArchive.Load(dir);
        Remember(id, o);
        return o;
    }

    public void Delete(Guid id)
    {
        lock (_gate)
        {
            var node = _lru.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value.id == id) _lru.Remove(node);
                node = next;
            }
        }
        string dir = _paths.RunDir(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private void Remember(Guid id, ScenarioOutcome o)
    {
        lock (_gate)
        {
            _lru.AddFirst((id, o));
            while (_lru.Count > Capacity) _lru.RemoveLast();
        }
    }

    public static readonly string[] Formats = { "shp", "kmz", "kml", "geojson", "cap", "pdf", "csv", "asc", "delft3d", "dualsphysics" };

    public (byte[] data, string contentType, string fileName) Export(Guid id, ScenarioOutcome o, string format)
    {
        format = format.ToLowerInvariant();
        string safeName = new string(o.Prepared.Spec.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (string.IsNullOrEmpty(safeName)) safeName = "scenario";
        string baseName = $"pralaydrishti_{safeName}_{id.ToString("N")[..8]}";
        string cacheDir = Path.Combine(_paths.RunDir(id), "exports");
        Directory.CreateDirectory(cacheDir);

        var (ext, type) = format switch
        {
            "shp" => (".zip", "application/zip"),
            "kmz" => (".kmz", "application/vnd.google-earth.kmz"),
            "kml" => (".kml", "application/vnd.google-earth.kml+xml"),
            "geojson" => (".geojson", "application/geo+json"),
            "cap" => (".cap.xml", "application/cap+xml"),
            "pdf" => (".pdf", "application/pdf"),
            "csv" => (".csv", "text/csv"),
            "asc" => (".asc.zip", "application/zip"),
            "delft3d" => ("_delft3dfm.zip", "application/zip"),
            "dualsphysics" => ("_dualsphysics.zip", "application/zip"),
            _ => throw new ArgumentException($"unknown export format '{format}'. Use one of: {string.Join(", ", Formats)}")
        };
        string cached = Path.Combine(cacheDir, format + ext);
        if (File.Exists(cached) && format != "cap") return (File.ReadAllBytes(cached), type, baseName + ext);

        byte[] data = format switch
        {
            "shp" => GisExporter.ShapefileZip(LayerBundle.From(o), safeName),
            "kmz" => KmlExporter.Kmz(o, LayerBundle.From(o), EventStart(id)),
            "kml" => Encoding.UTF8.GetBytes(KmlExporter.Kml(o, LayerBundle.From(o), EventStart(id))),
            "geojson" => Encoding.UTF8.GetBytes(GisExporter.GeoJson(LayerBundle.From(o))),
            "cap" => Encoding.UTF8.GetBytes(CapAlertBuilder.Build(o, id.ToString("N")[..12])),
            "pdf" => SitrepPdf.Build(o, id.ToString("N")[..8], DateTime.UtcNow),
            "csv" => Encoding.UTF8.GetBytes(GisExporter.Csv(o)),
            "asc" => AsciiZip(o),
            "delft3d" => Delft3DZip(o),
            "dualsphysics" => DualSphZip(o),
            _ => Array.Empty<byte>()
        };
        File.WriteAllBytes(cached, data);
        return (data, type, baseName + ext);
    }

    private DateTime EventStart(Guid id)
    {
        var f = new FileInfo(Path.Combine(_paths.RunDir(id), "meta.json"));
        return f.Exists ? f.CreationTimeUtc : DateTime.UtcNow;
    }

    private static byte[] AsciiZip(ScenarioOutcome o)
    {
        var g = o.FarField.Dem;
        var files = new Dictionary<string, string>
        {
            ["max_depth_m.asc"] = GisExporter.AsciiGrid(g, o.FarField.MaxDepth),
            ["max_velocity_ms.asc"] = GisExporter.AsciiGrid(g, o.FarField.MaxSpeed),
            ["arrival_min.asc"] = GisExporter.AsciiGrid(g, o.FarField.ArrivalTime.Select(a => float.IsNaN(a) ? float.NaN : a / 60f).ToArray()),
            ["hazard_class.asc"] = GisExporter.AsciiGrid(g, o.HazardClass.Select(b => (float)b).ToArray()),
            ["dem_used_m.asc"] = GisExporter.AsciiGrid(g, g.Z)
        };
        if (o.Ensemble is { } e) files["flood_probability.asc"] = GisExporter.AsciiGrid(g, e.Probability);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, content) in files)
            {
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(content);
                using (var w = new StreamWriter(zip.CreateEntry(Path.ChangeExtension(name, ".prj")).Open())) w.Write(GisExporter.WgsPrj);
            }
        return ms.ToArray();
    }

    private byte[] Delft3DZip(ScenarioOutcome o)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "pd_d3d_" + Guid.NewGuid().ToString("N"));
        try
        {
            var setup = new ScenarioEngine().BuildSetup(o.Prepared, 1.0, keepSnapshots: true);
            Delft3DFmAdapter.WriteModel(setup, tmp, "dambreak");
            return ZipDir(tmp);
        }
        finally { TryDelete(tmp); }
    }

    private byte[] DualSphZip(ScenarioOutcome o)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "pd_dsph_" + Guid.NewGuid().ToString("N"));
        try
        {
            var p = o.Prepared;
            var path = p.UpstreamPath.Concat(p.DownstreamPath.Skip(1)).ToList();
            var (ch, bed) = PralayDrishti.Infrastructure.Engine.Geo.Hydrology.Profile(p.OriginalDem, path);
            double offset = ch[Math.Max(0, p.UpstreamPath.Count - 1)];
            double H = p.Breach.Depth;
            double end = Math.Min(ch[^1] - offset, 15 * H);
            var xs = new List<double>();
            var zs = new List<double>();
            for (int i = 0; i < ch.Length; i++)
            {
                double x = ch[i] - offset;
                if (x < -3.5 * H || x > end) continue;
                xs.Add(x);
                zs.Add(bed[i]);
            }
            if (xs.Count < 2) { xs = new() { -3.5 * H, end }; zs = new() { p.BedAtDam, p.BedAtDam - 0.01 * end }; }
            var st = p.Spec.Structure;
            DualSphysicsExporter.Write(new DualSphysicsExporter.Case(xs.ToArray(), zs.ToArray(), 3 * H, p.BedAtDam + H,
                Math.Max(H / 40, 0.5), Math.Max(H / 4, 5), Math.Max(60, 12 * H / Math.Sqrt(9.81 * H)),
                new[] { 0, 2 * H, 4 * H, 8 * H }, st?.DistanceKm * 1000, st?.HeightM ?? 0, p.Spec.DebrisFlow ? 1800 : 1000), tmp);
            return ZipDir(tmp);
        }
        finally { TryDelete(tmp); }
    }

    private static byte[] ZipDir(string dir)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(f, Path.GetRelativePath(dir, f).Replace('\\', '/'));
        return ms.ToArray();
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* temp */ }
    }
}
