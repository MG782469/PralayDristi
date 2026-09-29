using System.Text.Json;
using System.Text.Json.Serialization;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.Infrastructure.Services;

/// <summary>
/// Finished runs: a small LRU in memory, everything else on disk (RunArchive). Behind IResultStore so the disk
/// can become object storage (MinIO / S3) when the workers move to Kubernetes.
/// </summary>
public sealed class ResultStore : IResultStore
{
    // same settings the prototype API used: NaN allowed, tuples (legends) as item1/item2, enums as strings
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    private const int Capacity = 4;
    private readonly DataPaths _paths;
    private readonly object _gate = new();
    private readonly LinkedList<(Guid id, ScenarioOutcome o)> _lru = new();

    public ResultStore(DataPaths paths) => _paths = paths;

    public bool Exists(Guid runId) => File.Exists(Path.Combine(_paths.RunDir(runId), "meta.json"));

    public void Put(Guid id, ScenarioOutcome o)
    {
        RunArchive.Save(o, _paths.RunDir(id));
        Remember(id, o);
    }

    public ScenarioOutcome? Get(Guid id)
    {
        lock (_gate)
        {
            for (var node = _lru.First; node != null; node = node.Next)
            {
                if (node.Value.id != id) continue;
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.o;
            }
        }
        if (!Exists(id)) return null;
        var o = RunArchive.Load(_paths.RunDir(id));
        Remember(id, o);
        return o;
    }

    public Task<JsonPayload?> GetDetailAsync(Guid runId, RunSummaryDto summary, CancellationToken ct = default)
    {
        var o = Get(runId);
        return Task.FromResult(o == null ? null : new JsonPayload(JsonSerializer.Serialize(RunDetailBuilder.Detail(summary, o), Json)));
    }

    public Task<JsonPayload?> GetProfileAsync(Guid runId, CancellationToken ct = default)
    {
        var o = Get(runId);
        if (o == null) return Task.FromResult<JsonPayload?>(null);
        var p = o.Prepared;
        var (ch, bed) = Hydrology.Profile(p.OriginalDem, p.DownstreamPath);
        var f = o.FarField;
        int step = Math.Max(1, ch.Length / 400);
        var rows = new List<object>();
        for (int k = 0; k < ch.Length; k += step)
        {
            int i = p.DownstreamPath[k];
            rows.Add(new
            {
                km = Math.Round(ch[k] / 1000, 3),
                bed = Math.Round(bed[k], 1),
                ws = float.IsNaN(f.MaxSurface[i]) ? (double?)null : Math.Round(f.MaxSurface[i], 1),
                depth = Math.Round(f.MaxDepth[i], 2),
                velocity = Math.Round(f.MaxSpeed[i], 2),
                arrivalMin = float.IsNaN(f.ArrivalTime[i]) ? (double?)null : Math.Round(f.ArrivalTime[i] / 60, 1)
            });
        }
        return Task.FromResult<JsonPayload?>(new JsonPayload(JsonSerializer.Serialize(rows, Json)));
    }

    public Task<JsonPayload?> SamplePointAsync(Guid runId, double lon, double lat, CancellationToken ct = default)
    {
        var o = Get(runId);
        if (o == null) return Task.FromResult<JsonPayload?>(null);
        var g = o.FarField.Dem;
        object result;
        if (!g.TryCell(new GeoPoint(lon, lat), out int r, out int c))
        {
            result = new { inside = false };
        }
        else
        {
            int i = g.Idx(r, c);
            float arr = o.FarField.ArrivalTime[i];
            result = new
            {
                inside = true,
                ground = Math.Round(o.Prepared.OriginalDem.Z[i], 1),
                depth = Math.Round(o.FarField.MaxDepth[i], 2),
                velocity = Math.Round(o.FarField.MaxSpeed[i], 2),
                surface = float.IsNaN(o.FarField.MaxSurface[i]) ? (double?)null : Math.Round(o.FarField.MaxSurface[i], 1),
                arrivalMin = float.IsNaN(arr) ? (double?)null : Math.Round(arr / 60, 1),
                peakMin = Math.Round(o.FarField.PeakTime[i] / 60, 1),
                hazard = o.HazardClass[i],
                probability = o.Ensemble is { } e ? Math.Round(e.Probability[i], 2) : (double?)null,
                excluded = o.Prepared.ExcludeMask[i],
                depthSeries = o.FarField.Snapshots.Select(s => new[] { Math.Round(s.Time / 60, 1), Math.Round(s.Depth[i], 2) })
            };
        }
        return Task.FromResult<JsonPayload?>(new JsonPayload(JsonSerializer.Serialize(result, Json)));
    }

    public IReadOnlyList<Guid> ListStoredRunIds() =>
        Directory.GetDirectories(_paths.Runs).Select(d => Guid.TryParseExact(Path.GetFileName(d), "N", out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).ToList();

    public void Delete(Guid runId)
    {
        lock (_gate)
        {
            var node = _lru.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value.id == runId) _lru.Remove(node);
                node = next;
            }
        }
        string dir = _paths.RunDir(runId);
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
}
