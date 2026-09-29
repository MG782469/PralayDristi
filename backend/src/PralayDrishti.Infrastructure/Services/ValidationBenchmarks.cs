using System.Text.Json;
using System.Text.Json.Nodes;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Solvers;

namespace PralayDrishti.Infrastructure.Services;

/// <summary>
/// Validation ladder: 1) exact solutions (Ritter, Stoker; Ritter for SPH), 2) benchmarks with observed gauges
/// (Malpasset and friends from data/benchmarks), 3) a real event against a Sentinel-1 flood map.
/// </summary>
public sealed class ValidationBenchmarks : IValidationBenchmarks
{
    private readonly DataPaths _paths;
    private readonly ResultStore _store;

    public ValidationBenchmarks(DataPaths paths, ResultStore store)
    {
        _paths = paths;
        _store = store;
    }

    private static JsonPayload Payload(object o) => new(JsonSerializer.Serialize(o, ResultStore.Json));

    public sealed record AnalyticResult(string Test, double H0, double H1, double T, double Dx, double L1, double L2, double Linf,
        List<double> X, List<double> Model, List<double> Exact);

    public JsonPayload Analytic(double h0, double h1, double t) => Payload(RunAnalytic(h0, h1, t));

    internal static AnalyticResult RunAnalytic(double h0, double h1, double t)
    {
        int nx = 400;
        double dx = Math.Max(0.5, 2 * Math.Sqrt(9.81 * h0) * t * 1.3 / (nx / 2.0));
        var dem = SyntheticTerrain.Flume(nx, 3, dx);
        double x0 = nx / 2 * dem.Dx;
        var init = new float[dem.Count];
        for (int c = 0; c < nx; c++)
            for (int r = 0; r < 3; r++)
                init[dem.Idx(r, c)] = (float)((c + 0.5) * dem.Dx < x0 ? h0 : h1);
        var res = new SweSolver(new SweSetup { Dem = dem, Manning = 0, InitialDepth = init, Duration = t, SnapshotInterval = t, MaxDt = 0.05 }).Run();
        var final = res.Snapshots[^1].Depth;
        var xs = new List<double>();
        var model = new List<double>();
        var exact = new List<double>();
        for (int c = 0; c < nx; c++)
        {
            double x = (c + 0.5) * dem.Dx - x0;
            xs.Add(Math.Round(x, 2));
            model.Add(Math.Round(final[dem.Idx(1, c)], 4));
            exact.Add(Math.Round(h1 > 0 ? AnalyticDamBreak.Stoker(h0, h1, x, res.SimulatedTime).h : AnalyticDamBreak.Ritter(h0, x, res.SimulatedTime).h, 4));
        }
        var (l1, l2, linf) = ValidationMetrics.Norms(model, exact);
        return new AnalyticResult(h1 > 0 ? "Stoker (wet bed)" : "Ritter (dry bed)", h0, h1, res.SimulatedTime, dem.Dx,
            Math.Round(l1, 4), Math.Round(l2, 4), Math.Round(linf, 3), xs, model, exact);
    }

    public Task<JsonPayload> SphCheckAsync(int resolution, double t, CancellationToken ct) => Task.Run(() => Payload(RunSph(resolution, t, ct)), ct);

    private static object RunSph(int resolution, double t, CancellationToken ct)
    {
        var res = new SphSolver(new SphSetup
        {
            ProfileX = new[] { -5.0, 15.0 }, ProfileZ = new[] { 0.0, 0.0 }, ReservoirDepth = 1, ReservoirLength = 2,
            DomainEnd = 8, ParticlesOverDepth = Math.Clamp(resolution, 10, 40), Duration = Math.Clamp(t, 0.2, 2), GaugeX = new[] { 0.0, 1.0, 2.0 }
        }).Run(null, ct);
        double tEnd = res.FrontTime[^1];
        double ritterFront = 2 * Math.Sqrt(9.81) * tEnd;
        return new
        {
            test = "SPH dam break on a dry flat bed vs Ritter",
            particles = res.FluidParticles,
            wallSeconds = Math.Round(res.WallTime.TotalSeconds, 1),
            frontSph = Math.Round(res.FrontX[^1], 3),
            frontRitter = Math.Round(ritterFront, 3),
            frontRatio = Math.Round(res.FrontX[^1] / ritterFront, 3),
            front = res.FrontTime.Zip(res.FrontX, (a, b) => new[] { Math.Round(a, 3), Math.Round(b, 3) }).ToList(),
            gauges = res.Gauges.Select(g => new
            {
                x = g.X,
                sph = g.Time.Zip(g.Depth, (a, b) => new[] { Math.Round(a, 3), Math.Round(b, 3) }).ToList(),
                ritter = g.Time.Select(a => new[] { Math.Round(a, 3), Math.Round(AnalyticDamBreak.Ritter(1, g.X, a).h, 3) }).ToList()
            }).ToList(),
            frame = new { x = res.Frames[^1].X.Select(v => Math.Round(v, 3)), z = res.Frames[^1].Z.Select(v => Math.Round(v, 3)) }
        };
    }

    public async Task<JsonPayload> ScoreboardAsync(CancellationToken ct)
    {
        var (ritter, stoker, sph) = await Task.Run(() => (RunAnalytic(10, 0, 8), RunAnalytic(10, 2, 8), RunSph(16, 1.0, ct)), ct);
        var sphNode = JsonNode.Parse(JsonSerializer.Serialize(sph, ResultStore.Json))!;
        var events = Directory.GetFiles(_paths.Benchmarks, "event_*.json").Select(f => JsonNode.Parse(File.ReadAllText(f))).Where(n => n != null).ToList();
        var benchmarks = Directory.GetDirectories(_paths.Benchmarks)
            .Where(d => File.Exists(Path.Combine(d, "benchmark.json")))
            .Select(d => new
            {
                name = Path.GetFileName(d),
                result = File.Exists(Path.Combine(d, "result.json")) ? JsonNode.Parse(File.ReadAllText(Path.Combine(d, "result.json"))) : null
            }).ToList();

        return Payload(new
        {
            analytic = new[] { ritter, stoker }.Select(a => new { a.Test, a.L1, a.L2, a.Linf }),
            sph = new { particles = sphNode["particles"], frontRatio = sphNode["frontRatio"], wallSeconds = sphNode["wallSeconds"] },
            benchmarks,
            malpassetHint = benchmarks.Any(b => b.name == "malpasset") ? null :
                "Malpasset data is not bundled. Put the DEM (EPSG:4326 GeoTIFF), reservoir polygon and the police-survey maximum water levels " +
                "in data/benchmarks/malpasset/benchmark.json (format in docs/VALIDATION.md), then run the benchmark.",
            events
        });
    }

    private sealed record ArrivalObs(string Name, double Lon, double Lat, double ObservedMin);

    public async Task<JsonPayload> CompareAsync(Guid runId, string observedGeoJson, string? permanentWaterGeoJson, string? arrivalsJson,
        string? eventName, double depthThreshold, CancellationToken ct)
    {
        var o = _store.Get(runId) ?? throw new NotFoundException("Run results", runId);
        var g = o.FarField.Dem;
        var obs = GeoJsonMask.Rasterize(g, observedGeoJson);
        if (!obs.Any(x => x)) throw new BusinessRuleException("Observed polygons do not overlap the model domain.");
        var ignore = (bool[])o.Prepared.ExcludeMask.Clone();
        if (!string.IsNullOrWhiteSpace(permanentWaterGeoJson))
        {
            var perm = GeoJsonMask.Rasterize(g, permanentWaterGeoJson);
            for (int i = 0; i < ignore.Length; i++) ignore[i] |= perm[i];
        }
        var sim = ValidationMetrics.WetMask(o.FarField.MaxDepth, depthThreshold);
        var ctab = ValidationMetrics.Compare(sim, obs, ignore);

        double? rmse = null, bias = null;
        int nArr = 0;
        if (!string.IsNullOrWhiteSpace(arrivalsJson))
        {
            var list = JsonSerializer.Deserialize<List<ArrivalObs>>(arrivalsJson, ResultStore.Json) ?? new();
            var arr = list.Select(a => g.CellIndex(new GeoPoint(a.Lon, a.Lat)) is int ci ? new ArrivalObservation(a.Name, ci, a.ObservedMin) : null)
                .OfType<ArrivalObservation>().ToList();
            var (r, b, n) = ValidationMetrics.ArrivalError(o.FarField.ArrivalTime, arr);
            if (n > 0) { rmse = Math.Round(r, 1); bias = Math.Round(b, 1); nArr = n; }
        }

        var result = new
        {
            runId, eventName = eventName ?? o.Prepared.Spec.Name, evaluatedUtc = DateTime.UtcNow,
            hits = ctab.Hits, falseAlarms = ctab.FalseAlarms, misses = ctab.Misses,
            csi = Math.Round(ctab.Csi, 3), f1 = Math.Round(ctab.F1, 3), pod = Math.Round(ctab.Pod, 3), far = Math.Round(ctab.Far, 3),
            bias = Math.Round(ctab.Bias, 3), arrivalRmseMin = rmse, arrivalBiasMin = bias, arrivalPoints = nArr
        };
        string json = JsonSerializer.Serialize(result, ResultStore.Json);
        await File.WriteAllTextAsync(Path.Combine(_paths.Benchmarks, $"event_{runId:N}.json"), json, ct);
        return new JsonPayload(json);
    }

    private sealed class BenchmarkFile
    {
        public string Dem { get; set; } = "";
        public double InitialLevel { get; set; }
        public List<double[]> Reservoir { get; set; } = new();
        public double DurationHours { get; set; } = 1;
        public double Manning { get; set; } = 0.033;
        public List<BenchmarkGauge> Gauges { get; set; } = new();
    }

    private sealed class BenchmarkGauge
    {
        public string Name { get; set; } = "";
        public double Lon { get; set; }
        public double Lat { get; set; }
        public double? ObservedMaxLevel { get; set; }
        public double? ObservedArrivalMin { get; set; }
    }

    public async Task<JsonPayload> RunBenchmarkAsync(string name, CancellationToken ct)
    {
        if (name.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')) throw new BusinessRuleException("Bad benchmark name.");
        string dir = Path.Combine(_paths.Benchmarks, name);
        string file = Path.Combine(dir, "benchmark.json");
        if (!File.Exists(file)) throw new NotFoundException("Benchmark", name);
        var b = JsonSerializer.Deserialize<BenchmarkFile>(await File.ReadAllTextAsync(file, ct), ResultStore.Json)!;
        var dem = GeoTiffReader.Read(Path.Combine(dir, b.Dem));
        dem = dem.Coarsen(dem.SuggestFactor(300_000));
        var mask = RasterOps.PolygonMask(dem, b.Reservoir.Select(p => new GeoPoint(p[0], p[1])).ToList());
        var init = new float[dem.Count];
        for (int i = 0; i < dem.Count; i++)
            if (mask[i]) init[i] = (float)Math.Max(0, b.InitialLevel - dem.Z[i]);
        var gauges = b.Gauges.Select(gg => dem.CellIndex(new GeoPoint(gg.Lon, gg.Lat)) is int ci ? new Gauge { Name = gg.Name, Cell = ci } : null)
            .OfType<Gauge>().ToList();
        var res = await Task.Run(() => new SweSolver(new SweSetup
        {
            Dem = dem, Manning = b.Manning, InitialDepth = init, Duration = b.DurationHours * 3600, Gauges = gauges,
            KeepSnapshots = false, ArrivalDepth = 0.1
        }).Run(null, ct), ct);

        var rows = b.Gauges.Select(gg =>
        {
            var ci = dem.CellIndex(new GeoPoint(gg.Lon, gg.Lat));
            double? mx = ci is int i && !float.IsNaN(res.MaxSurface[i]) ? Math.Round(res.MaxSurface[i], 2) : null;
            double? arr = ci is int j && !float.IsNaN(res.ArrivalTime[j]) ? Math.Round(res.ArrivalTime[j] / 60, 1) : null;
            double? err = mx is double m && gg.ObservedMaxLevel is double ob ? Math.Round(m - ob, 2) : null;
            return new { gg.Name, modelMax = mx, gg.ObservedMaxLevel, error = err, modelArrival = arr, gg.ObservedArrivalMin };
        }).ToList();
        var errs = rows.Where(r => r.error != null).Select(r => r.error!.Value).ToList();
        string json = JsonSerializer.Serialize(new
        {
            benchmark = name, cells = dem.Count, wallSec = Math.Round(res.WallTime.TotalSeconds, 1),
            maxLevelRmse = errs.Count > 0 ? Math.Round(Math.Sqrt(errs.Average(e => e * e)), 2) : (double?)null,
            meanError = errs.Count > 0 ? Math.Round(errs.Average(), 2) : (double?)null,
            gauges = rows
        }, ResultStore.Json);
        await File.WriteAllTextAsync(Path.Combine(dir, "result.json"), json, ct);
        return new JsonPayload(json);
    }
}
