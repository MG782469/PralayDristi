using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using PralayDrishti.Core.Analysis;
using PralayDrishti.Core.Geo;
using PralayDrishti.Core.Hydraulics;
using PralayDrishti.Core.Solvers;

namespace PralayDrishti.Core.Pipeline;

/// <summary>
/// Saves a finished run to disk (JSON for the small stuff, gzipped float blobs for grids) and loads it back.
/// Runs survive a restart of the service, and exports can be regenerated at any time.
/// </summary>
public static class RunArchive
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class HydroDto
    {
        public List<double> T { get; set; } = new();
        public List<double> Q { get; set; } = new();
        public List<double> L { get; set; } = new();
        public string Source { get; set; } = "";

        public static HydroDto From(Hydrograph h) => new() { T = h.Time, Q = h.Discharge, L = h.Level, Source = h.Source };

        public Hydrograph To()
        {
            var h = new Hydrograph { Source = Source };
            for (int i = 0; i < T.Count; i++) h.Add(T[i], Q[i], i < L.Count ? L[i] : double.NaN);
            return h;
        }
    }

    private sealed class GaugeDto
    {
        public string Name { get; set; } = "";
        public int Cell { get; set; }
        public List<float> T { get; set; } = new();
        public List<float> D { get; set; } = new();
        public List<float> V { get; set; } = new();
        public List<float> S { get; set; } = new();
    }

    private sealed class Meta
    {
        public int Cols { get; set; }
        public int Rows { get; set; }
        public double West { get; set; }
        public double North { get; set; }
        public double CellSize { get; set; }
        public ScenarioSpec Spec { get; set; } = new();
        public double DamLon { get; set; }
        public double DamLat { get; set; }
        public int DamCell { get; set; }
        public List<int> Down { get; set; } = new();
        public List<int> Up { get; set; } = new();
        public int[] Inflow { get; set; } = Array.Empty<int>();
        public double DirE { get; set; }
        public double DirN { get; set; }
        public BreachGeometry Breach { get; set; } = null!;
        public HydroDto Hyd { get; set; } = new();
        public HydroDto Routed { get; set; } = new();
        public List<StoragePoint> Storage { get; set; } = new();
        public double BedAtDam { get; set; }
        public List<EmpiricalPeak> Peaks { get; set; } = new();
        public List<string> Log { get; set; } = new();
        public ImpactReport Impact { get; set; } = null!;
        public NearFieldResult? Near { get; set; }
        public List<CascadeEvent> Cascade { get; set; } = new();
        public string Engine { get; set; } = "";
        public Dictionary<string, double> Timings { get; set; } = new();
        public List<GaugeDto> Gauges { get; set; } = new();
        public List<double> SnapTimes { get; set; } = new();
        public List<(string name, double time)> Failures { get; set; } = new();
        public double InflowVolume { get; set; }
        public double InitialVolume { get; set; }
        public double FinalVolume { get; set; }
        public double OutflowVolume { get; set; }
        public long Steps { get; set; }
        public double SimTime { get; set; }
        public double WallSec { get; set; }
        public int EnsMembers { get; set; }
        public List<EnsembleMember> EnsSamples { get; set; } = new();
        public List<double> EnsAreas { get; set; } = new();
        public double EnsThreshold { get; set; }
        public double? NatVolume { get; set; }
        public double? NatArea { get; set; }
        public double? NatCrest { get; set; }
        public double? NatHeight { get; set; }
        public List<string>? NatNotes { get; set; }
    }

    public static void Save(ScenarioOutcome o, string dir)
    {
        Directory.CreateDirectory(dir);
        var p = o.Prepared;
        var f = o.FarField;
        var meta = new Meta
        {
            Cols = p.Dem.Cols, Rows = p.Dem.Rows, West = p.Dem.West, North = p.Dem.North, CellSize = p.Dem.CellSize,
            Spec = p.Spec, DamLon = p.DamPoint.Lon, DamLat = p.DamPoint.Lat, DamCell = p.DamCell,
            Down = p.DownstreamPath, Up = p.UpstreamPath, Inflow = p.InflowCells, DirE = p.FlowDir.east, DirN = p.FlowDir.north,
            Breach = p.Breach, Hyd = HydroDto.From(p.Hydrograph), Routed = HydroDto.From(p.RoutedHydrograph),
            Storage = p.Storage.Points.ToList(), BedAtDam = p.BedAtDam, Peaks = p.EmpiricalPeaks, Log = p.Log,
            Impact = o.Impact, Near = o.NearField, Cascade = o.Cascade, Engine = o.EngineName, Timings = o.TimingsSec,
            Gauges = f.Gauges.Select(g => new GaugeDto { Name = g.Name, Cell = g.Cell, T = g.Time, D = g.Depth, V = g.Speed, S = g.Surface }).ToList(),
            SnapTimes = f.Snapshots.Select(s => s.Time).ToList(),
            Failures = f.Failures,
            InflowVolume = f.InflowVolume, InitialVolume = f.InitialVolume, FinalVolume = f.FinalVolume, OutflowVolume = f.OutflowVolume,
            Steps = f.Steps, SimTime = f.SimulatedTime, WallSec = f.WallTime.TotalSeconds,
            EnsMembers = o.Ensemble?.Members ?? 0, EnsSamples = o.Ensemble?.Samples ?? new(), EnsAreas = o.Ensemble?.FloodedAreaKm2 ?? new(),
            EnsThreshold = o.Ensemble?.Threshold ?? 0,
            NatVolume = p.NaturalDam?.LakeVolumeM3, NatArea = p.NaturalDam?.LakeAreaM2, NatCrest = p.NaturalDam?.CrestElevation,
            NatHeight = p.NaturalDam?.DamHeight, NatNotes = p.NaturalDam?.Notes
        };
        File.WriteAllText(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(meta, Json));

        var grids = new Dictionary<string, float[]>
        {
            ["dem"] = p.Dem.Z, ["dem0"] = p.OriginalDem.Z, ["manning"] = p.ManningField,
            ["maxDepth"] = f.MaxDepth, ["maxSpeed"] = f.MaxSpeed, ["maxDv"] = f.MaxDv, ["arrival"] = f.ArrivalTime,
            ["peak"] = f.PeakTime, ["maxWs"] = f.MaxSurface, ["hazard"] = o.HazardClass.Select(b => (float)b).ToArray(),
            ["exclude"] = p.ExcludeMask.Select(b => b ? 1f : 0f).ToArray()
        };
        if (p.InitialDepth != null) grids["init"] = p.InitialDepth;
        if (o.Ensemble is { } e)
        {
            grids["ensProb"] = e.Probability; grids["ensP50"] = e.P50Depth; grids["ensP90"] = e.P90Depth; grids["ensMean"] = e.MeanDepth;
        }
        for (int k = 0; k < f.Snapshots.Count; k++) grids[$"snap{k}"] = f.Snapshots[k].Depth;
        WriteGrids(Path.Combine(dir, "grids.bin.gz"), grids);
    }

    public static ScenarioOutcome Load(string dir)
    {
        var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(Path.Combine(dir, "meta.json")), Json)
                   ?? throw new InvalidDataException("bad meta.json");
        var grids = ReadGrids(Path.Combine(dir, "grids.bin.gz"));
        var dem = new DemGrid(meta.Cols, meta.Rows, meta.West, meta.North, meta.CellSize, grids["dem"]);
        var dem0 = new DemGrid(meta.Cols, meta.Rows, meta.West, meta.North, meta.CellSize, grids["dem0"]);
        var exclude = grids["exclude"].Select(v => v > 0.5f).ToArray();

        NaturalDamResult? nat = null;
        if (meta.NatVolume is double nv)
            nat = new NaturalDamResult(meta.BedAtDam, meta.NatCrest ?? 0, meta.NatHeight ?? 0, meta.NatArea ?? 0, nv, 0, null, null,
                false, false, -1, meta.DamCell, new List<int>(), new StorageCurve(meta.Storage), exclude, meta.NatNotes ?? new());

        var prep = new PreparedScenario
        {
            Spec = meta.Spec, Dem = dem, OriginalDem = dem0, DamPoint = new GeoPoint(meta.DamLon, meta.DamLat), DamCell = meta.DamCell,
            DownstreamPath = meta.Down, UpstreamPath = meta.Up, InflowCells = meta.Inflow, FlowDir = (meta.DirE, meta.DirN),
            Breach = meta.Breach, Hydrograph = meta.Hyd.To(), RoutedHydrograph = meta.Routed.To(),
            Storage = new StorageCurve(meta.Storage), BedAtDam = meta.BedAtDam, ManningField = grids["manning"],
            ExcludeMask = exclude, EmpiricalPeaks = meta.Peaks, NaturalDam = nat,
            InitialDepth = grids.GetValueOrDefault("init"), Log = meta.Log
        };

        var gauges = meta.Gauges.Select(g =>
        {
            var gg = new Gauge { Name = g.Name, Cell = g.Cell };
            gg.Time.AddRange(g.T); gg.Depth.AddRange(g.D); gg.Speed.AddRange(g.V); gg.Surface.AddRange(g.S);
            return gg;
        }).ToList();
        var snaps = meta.SnapTimes.Select((t, k) => new Snapshot(t, grids[$"snap{k}"])).ToList();
        var far = new SweResult
        {
            Dem = dem, MaxDepth = grids["maxDepth"], MaxSpeed = grids["maxSpeed"], MaxDv = grids["maxDv"],
            ArrivalTime = grids["arrival"], PeakTime = grids["peak"], MaxSurface = grids["maxWs"], Snapshots = snaps,
            Gauges = gauges, Failures = meta.Failures, InflowVolume = meta.InflowVolume, InitialVolume = meta.InitialVolume,
            FinalVolume = meta.FinalVolume, OutflowVolume = meta.OutflowVolume, Steps = meta.Steps,
            SimulatedTime = meta.SimTime, WallTime = TimeSpan.FromSeconds(meta.WallSec)
        };

        EnsembleResult? ens = null;
        if (meta.EnsMembers > 0 && grids.ContainsKey("ensProb"))
            ens = new EnsembleResult
            {
                Members = meta.EnsMembers, Probability = grids["ensProb"], P50Depth = grids["ensP50"], P90Depth = grids["ensP90"],
                MeanDepth = grids["ensMean"], Samples = meta.EnsSamples, FloodedAreaKm2 = meta.EnsAreas, Threshold = meta.EnsThreshold
            };

        return new ScenarioOutcome
        {
            Prepared = prep, FarField = far, Impact = meta.Impact, HazardClass = grids["hazard"].Select(v => (byte)v).ToArray(),
            NearField = meta.Near, Ensemble = ens, Cascade = meta.Cascade, EngineName = meta.Engine, TimingsSec = meta.Timings
        };
    }

    private static void WriteGrids(string path, Dictionary<string, float[]> grids)
    {
        using var fs = File.Create(path);
        using var gz = new GZipStream(fs, CompressionLevel.Fastest);
        using var w = new BinaryWriter(gz);
        w.Write(grids.Count);
        foreach (var (name, data) in grids)
        {
            w.Write(name);
            w.Write(data.Length);
            var bytes = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            w.Write(bytes);
        }
    }

    private static Dictionary<string, float[]> ReadGrids(string path)
    {
        using var fs = File.OpenRead(path);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var r = new BinaryReader(gz);
        int n = r.ReadInt32();
        var d = new Dictionary<string, float[]>();
        for (int k = 0; k < n; k++)
        {
            string name = r.ReadString();
            int len = r.ReadInt32();
            var bytes = r.ReadBytes(len * 4);
            var arr = new float[len];
            Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
            d[name] = arr;
        }
        return d;
    }
}
