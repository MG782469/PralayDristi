using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using PralayDrishti.Api.Services;
using PralayDrishti.Core.Analysis;
using PralayDrishti.Core.Geo;
using PralayDrishti.Core.Pipeline;
using PralayDrishti.Core.Solvers;

namespace PralayDrishti.Api.Controllers;

/// <summary>
/// Validation scoreboard, three levels:
///   1. analytic (Ritter dry bed, Stoker wet bed) for the SWE solver, Ritter for SPH
///   2. benchmark (Malpasset 1959 or any DEM + reservoir + observed gauge levels in data/benchmarks)
///   3. real event: simulated extent vs Sentinel-1 flood map (CSI/IoU, F1, POD, FAR, arrival error)
/// </summary>
[ApiController]
[Route("api/validation")]
public class ValidationController : ControllerBase
{
    private readonly RunStore _store;
    private readonly DataPaths _paths;

    public ValidationController(RunStore store, DataPaths paths)
    {
        _store = store;
        _paths = paths;
    }

    public sealed record AnalyticResult(string Test, double H0, double H1, double T, double Dx, double L1, double L2, double Linf,
        List<double> X, List<double> Model, List<double> Exact);

    public sealed record SphCheck(string Test, int Particles, double WallSeconds, double FrontSph, double FrontRitter, double FrontRatio,
        object Front, object Gauges, object Frame);

    [HttpGet("analytic")]
    public ActionResult<AnalyticResult> Analytic(double h0 = 10, double h1 = 0, double t = 8)
    {
        if (h0 <= 0 || h1 < 0 || h1 >= h0 || t <= 0 || t > 60) return BadRequest("need h0 > h1 >= 0 and 0 < t <= 60");
        return RunAnalytic(h0, h1, t);
    }

    private static AnalyticResult RunAnalytic(double h0, double h1, double t)
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

    [HttpGet("sph")]
    public async Task<SphCheck> Sph(int resolution = 20, double t = 1.0, CancellationToken ct = default) =>
        await Task.Run(() => RunSph(resolution, t, ct), ct);

    private static SphCheck RunSph(int resolution, double t, CancellationToken ct)
    {
        resolution = Math.Clamp(resolution, 10, 40);
        var res = new SphSolver(new SphSetup
        {
            ProfileX = new[] { -5.0, 15.0 }, ProfileZ = new[] { 0.0, 0.0 }, ReservoirDepth = 1, ReservoirLength = 2,
            DomainEnd = 8, ParticlesOverDepth = resolution, Duration = Math.Clamp(t, 0.2, 2), GaugeX = new[] { 0.0, 1.0, 2.0 }
        }).Run(null, ct);
        double tEnd = res.FrontTime[^1];
        double ritterFront = 2 * Math.Sqrt(9.81) * tEnd;
        return new SphCheck(
            "SPH dam break on a dry flat bed vs Ritter",
            res.FluidParticles,
            Math.Round(res.WallTime.TotalSeconds, 1),
            Math.Round(res.FrontX[^1], 3),
            Math.Round(ritterFront, 3),
            Math.Round(res.FrontX[^1] / ritterFront, 3),
            res.FrontTime.Zip(res.FrontX, (a, b) => new[] { Math.Round(a, 3), Math.Round(b, 3) }).ToList(),
            res.Gauges.Select(g => new
            {
                x = g.X,
                sph = g.Time.Zip(g.Depth, (a, b) => new[] { Math.Round(a, 3), Math.Round(b, 3) }).ToList(),
                ritter = g.Time.Select(a => new[] { Math.Round(a, 3), Math.Round(AnalyticDamBreak.Ritter(1, g.X, a).h, 3) }).ToList()
            }).ToList(),
            new { x = res.Frames[^1].X.Select(v => Math.Round(v, 3)), z = res.Frames[^1].Z.Select(v => Math.Round(v, 3)) });
    }

    public sealed record CompareRequest(JsonObject Observed, JsonObject? PermanentWater, List<ArrivalObs>? Arrivals, string? EventName,
        double DepthThreshold = 0.3);

    public sealed record ArrivalObs(string Name, double Lon, double Lat, double ObservedMin);

    /// <summary>Real-event validation: simulated extent vs Sentinel-1 flood polygons (from gee/s1_flood_map.py).</summary>
    [HttpPost("compare/{runId:guid}")]
    public IActionResult Compare(Guid runId, CompareRequest req)
    {
        var o = _store.Get(runId);
        if (o == null) return NotFound();
        var g = o.FarField.Dem;
        var obs = RasterizeGeoJson(g, req.Observed);
        if (!obs.Any(x => x)) return BadRequest("observed polygons do not overlap the model domain");
        var ignore = (bool[])o.Prepared.ExcludeMask.Clone();
        if (req.PermanentWater != null)
        {
            var perm = RasterizeGeoJson(g, req.PermanentWater);
            for (int i = 0; i < ignore.Length; i++) ignore[i] |= perm[i];
        }
        // only judge inside the envelope of what either the model or the satellite says is wet (plus a margin);
        // otherwise thousands of dry mountain cells inflate "accuracy"
        var sim = ValidationMetrics.WetMask(o.FarField.MaxDepth, req.DepthThreshold);
        var ct = ValidationMetrics.Compare(sim, obs, ignore);

        double? rmse = null, bias = null;
        int nArr = 0;
        if (req.Arrivals is { Count: > 0 })
        {
            var arr = req.Arrivals.Select(a => g.CellIndex(new GeoPoint(a.Lon, a.Lat)) is int ci ? new ArrivalObservation(a.Name, ci, a.ObservedMin) : null)
                .OfType<ArrivalObservation>().ToList();
            var (r, b, n) = ValidationMetrics.ArrivalError(o.FarField.ArrivalTime, arr);
            if (n > 0) { rmse = Math.Round(r, 1); bias = Math.Round(b, 1); nArr = n; }
        }

        var result = new
        {
            runId, eventName = req.EventName ?? o.Prepared.Spec.Name, evaluatedUtc = DateTime.UtcNow,
            hits = ct.Hits, falseAlarms = ct.FalseAlarms, misses = ct.Misses,
            csi = Math.Round(ct.Csi, 3), f1 = Math.Round(ct.F1, 3), pod = Math.Round(ct.Pod, 3), far = Math.Round(ct.Far, 3),
            bias = Math.Round(ct.Bias, 3), arrivalRmseMin = rmse, arrivalBiasMin = bias, arrivalPoints = nArr
        };
        System.IO.File.WriteAllText(Path.Combine(_paths.Benchmarks, $"event_{runId:N}.json"), JsonSerializer.Serialize(result, RunArchive.Json));
        return Ok(result);
    }

    private static (AnalyticResult ritter, AnalyticResult stoker, SphCheck sph)? _analyticCache;

    [HttpGet("scoreboard")]
    public async Task<IActionResult> Scoreboard(CancellationToken ct)
    {
        // the analytic checks are deterministic - compute once per process
        _analyticCache ??= await Task.Run(() => (RunAnalytic(10, 0, 8), RunAnalytic(10, 2, 8), RunSph(16, 1.0, ct)), ct);
        var (ritter, stoker, sph) = _analyticCache.Value;

        var events = Directory.GetFiles(_paths.Benchmarks, "event_*.json")
            .Select(f => JsonNode.Parse(System.IO.File.ReadAllText(f)))
            .Where(n => n != null)
            .ToList();

        var benchmarks = Directory.GetDirectories(_paths.Benchmarks)
            .Where(d => System.IO.File.Exists(Path.Combine(d, "benchmark.json")))
            .Select(d => new
            {
                name = Path.GetFileName(d),
                result = System.IO.File.Exists(Path.Combine(d, "result.json")) ? JsonNode.Parse(System.IO.File.ReadAllText(Path.Combine(d, "result.json"))) : null
            }).ToList();

        return Ok(new
        {
            analytic = new[] { ritter, stoker }.Select(a => new { a.Test, a.L1, a.L2, a.Linf }),
            sph = new { sph.Particles, sph.FrontRatio, sph.WallSeconds },
            benchmarks,
            malpassetHint = benchmarks.Any(b => b.name == "malpasset") ? null :
                "Malpasset data is not bundled. Put the DEM (EPSG:4326 GeoTIFF), reservoir polygon and the police-survey maximum water levels " +
                "in data/benchmarks/malpasset/benchmark.json (format in docs/VALIDATION.md), then POST /api/validation/benchmark/malpasset.",
            events
        });
    }

    public sealed class BenchmarkFile
    {
        public string Dem { get; set; } = "";
        public double InitialLevel { get; set; }
        public List<double[]> Reservoir { get; set; } = new();
        public double DurationHours { get; set; } = 1;
        public double Manning { get; set; } = 0.033;
        public List<BenchmarkGauge> Gauges { get; set; } = new();
    }

    public sealed class BenchmarkGauge
    {
        public string Name { get; set; } = "";
        public double Lon { get; set; }
        public double Lat { get; set; }
        public double? ObservedMaxLevel { get; set; }
        public double? ObservedArrivalMin { get; set; }
    }

    /// <summary>Generic "DEM + reservoir polygon + observed gauges" benchmark runner (Malpasset and friends).</summary>
    [HttpPost("benchmark/{name}")]
    public async Task<IActionResult> Benchmark(string name, CancellationToken ct)
    {
        if (name.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')) return BadRequest("bad name");
        string dir = Path.Combine(_paths.Benchmarks, name);
        string file = Path.Combine(dir, "benchmark.json");
        if (!System.IO.File.Exists(file)) return NotFound($"{file} not found");
        var b = JsonSerializer.Deserialize<BenchmarkFile>(await System.IO.File.ReadAllTextAsync(file, ct),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
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
            if (ci is not int i) return new { gg.Name, modelMax = (double?)null, gg.ObservedMaxLevel, error = (double?)null, modelArrival = (double?)null, gg.ObservedArrivalMin };
            double? mx = float.IsNaN(res.MaxSurface[i]) ? null : Math.Round(res.MaxSurface[i], 2);
            double? arr = float.IsNaN(res.ArrivalTime[i]) ? null : Math.Round(res.ArrivalTime[i] / 60, 1);
            return new { gg.Name, modelMax = mx, gg.ObservedMaxLevel, error = mx is double m && gg.ObservedMaxLevel is double ob ? Math.Round(m - ob, 2) : (double?)null, modelArrival = arr, gg.ObservedArrivalMin };
        }).ToList();
        var errs = rows.Where(r => r.error != null).Select(r => r.error!.Value).ToList();
        var summary = new
        {
            benchmark = name, cells = dem.Count, wallSec = Math.Round(res.WallTime.TotalSeconds, 1),
            maxLevelRmse = errs.Count > 0 ? Math.Round(Math.Sqrt(errs.Average(e => e * e)), 2) : (double?)null,
            meanError = errs.Count > 0 ? Math.Round(errs.Average(), 2) : (double?)null,
            gauges = rows
        };
        await System.IO.File.WriteAllTextAsync(Path.Combine(dir, "result.json"), JsonSerializer.Serialize(summary, RunArchive.Json), ct);
        return Ok(summary);
    }

    /// <summary>GeoJSON (FeatureCollection / Feature / Polygon / MultiPolygon) -> cell mask on the given grid.</summary>
    public static bool[] RasterizeGeoJson(DemGrid g, JsonObject node)
    {
        var mask = new bool[g.Count];
        void Polygon(JsonArray rings)
        {
            var outer = rings[0]!.AsArray().Select(c => new GeoPoint(c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToList();
            var m = RasterOps.PolygonMask(g, outer);
            for (int k = 1; k < rings.Count; k++)
            {
                var hole = rings[k]!.AsArray().Select(c => new GeoPoint(c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToList();
                var hm = RasterOps.PolygonMask(g, hole);
                for (int i = 0; i < m.Length; i++) if (hm[i]) m[i] = false;
            }
            for (int i = 0; i < m.Length; i++) if (m[i]) mask[i] = true;
        }
        void Geometry(JsonNode? geom)
        {
            if (geom == null) return;
            switch (geom["type"]?.GetValue<string>())
            {
                case "Polygon": Polygon(geom["coordinates"]!.AsArray()); break;
                case "MultiPolygon": foreach (var p in geom["coordinates"]!.AsArray()) Polygon(p!.AsArray()); break;
                case "GeometryCollection": foreach (var gg in geom["geometries"]!.AsArray()) Geometry(gg); break;
            }
        }
        switch (node["type"]?.GetValue<string>())
        {
            case "FeatureCollection": foreach (var f in node["features"]!.AsArray()) Geometry(f?["geometry"]); break;
            case "Feature": Geometry(node["geometry"]); break;
            default: Geometry(node); break;
        }
        return mask;
    }
}
