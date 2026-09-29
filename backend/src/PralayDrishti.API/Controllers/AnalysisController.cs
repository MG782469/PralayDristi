using PralayDrishti.Domain.Enums;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using PralayDrishti.API.Services;
using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Export;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Hydraulics;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.API.Controllers;

[ApiController]
[Route("api/analysis")]
public class AnalysisController : ControllerBase
{
    private readonly DemProvider _dem;
    private readonly RunStore _store;
    private readonly AppOptions _opt;

    public AnalysisController(DemProvider dem, RunStore store, AppOptions opt)
    {
        _dem = dem;
        _store = store;
        _opt = opt;
    }

    public sealed record NaturalDamRequest(List<double[]> Polygon, double? CrestElevation, double? HeightAboveBed, double? CurrentLevel,
        string Type = "LandslideDam");

    /// <summary>Natural Dam mode, step 1: how big can the lake get behind the debris the user drew?</summary>
    [HttpPost("natural-dam")]
    public async Task<IActionResult> NaturalDam(NaturalDamRequest req, CancellationToken ct)
    {
        if (req.Polygon is not { Count: >= 3 }) return BadRequest("polygon needs at least 3 vertices");
        var ring = req.Polygon.Select(p => new GeoPoint(p[0], p[1])).ToList();
        var center = new GeoPoint(ring.Average(p => p.Lon), ring.Average(p => p.Lat));
        var src = await _dem.ForAreaAsync(center, 6, 12, _opt.DefaultDemZoom, ct);
        var dem = src.Dem.Coarsen(src.Dem.SuggestFactor(400_000));
        var res = await Task.Run(() => NaturalDamAnalyzer.Analyze(dem,
            new NaturalDamAnalyzer.Request(ring, req.CrestElevation, req.HeightAboveBed, req.CurrentLevel)), ct);

        var type = ScenarioFactory.ParseType(req.Type);
        double level = res.CurrentLevel is double cl ? Math.Min(cl, res.CrestElevation) : res.CrestElevation;
        double vol = res.CurrentVolumeM3 ?? res.LakeVolumeM3;
        var input = new DamInput(type, res.DamHeight, level - res.BedElevation, Math.Max(vol, 1), FailureMode.Overtopping, 50, 200);
        var breach = BreachModels.LandslideDam(input);
        var hyd = BreachRouting.Route(res.Curve, breach, new BreachRouting.Options(res.CrestElevation, res.BedElevation, level,
            Dt: Math.Clamp(breach.FormationTime / 500, 0.5, 5)));

        var lakeSet = new HashSet<int>(res.LakeCells);
        var lakeGeom = Vectorizer.Polygonize(dem, i => lakeSet.Contains(i), 1.0);
        return Ok(new
        {
            demSource = src.Description,
            synthetic = src.Synthetic,
            bed = Math.Round(res.BedElevation, 1),
            spillLevel = Math.Round(res.CrestElevation, 1),
            damHeight = Math.Round(res.DamHeight, 1),
            lakeAreaKm2 = Math.Round(res.LakeAreaM2 / 1e6, 3),
            lakeVolumeMcm = Math.Round(res.LakeVolumeM3 / 1e6, 3),
            currentVolumeMcm = res.CurrentVolumeM3 is double c ? Math.Round(c / 1e6, 3) : (double?)null,
            maxLakeDepth = Math.Round(res.MaxLakeDepth, 1),
            touchesEdge = res.LakeTouchesDemEdge,
            saddleSpill = res.SpillLoweredBySaddle,
            notes = res.Notes,
            curve = res.Curve.Points.Select(p => new { elevation = Math.Round(p.Elevation, 1), areaKm2 = Math.Round(p.AreaM2 / 1e6, 4), volumeMcm = Math.Round(p.VolumeM3 / 1e6, 4) }),
            breach = new { breach.Method, breach.AverageWidth, formationMin = breach.FormationTime / 60, breach.EmpiricalPeak },
            peakDischarge = Math.Round(hyd.Peak),
            hydrograph = hyd.Decimate(200).Time.Zip(hyd.Decimate(200).Discharge, (t, q) => new[] { Math.Round(t / 60, 1), Math.Round(q) }),
            lake = lakeGeom == null ? null : JsonNode.Parse(GeoJsonOf(lakeGeom))
        });
    }

    public sealed record ReservoirRequest(double Lon, double Lat, double? AreaKm2, double? AltimetryLevel, double DeadStorageMcm = 0, double MaxRise = 300);

    /// <summary>Transboundary / no-ground-data reservoirs: satellite area (or altimetry level) + DEM -> volume.</summary>
    [HttpPost("reservoir-estimate")]
    public async Task<IActionResult> Reservoir(ReservoirRequest req, CancellationToken ct)
    {
        if (req.AreaKm2 is null && req.AltimetryLevel is null) return BadRequest("give areaKm2 (from NDWI) or altimetryLevel (SWOT/ICESat-2)");
        var p = new GeoPoint(req.Lon, req.Lat);
        double r = req.AreaKm2 is double a ? Math.Clamp(Math.Sqrt(a) * 2.5, 6, 40) : 15;
        var src = await _dem.ForAreaAsync(p, r, r, _opt.DefaultDemZoom, ct);
        var dem = src.Dem.Coarsen(src.Dem.SuggestFactor(500_000));
        var res = await Task.Run(() => SatelliteReservoirEstimator.Estimate(dem,
            new SatelliteReservoirEstimator.Request(p, req.AreaKm2, req.AltimetryLevel, req.MaxRise, req.DeadStorageMcm * 1e6)), ct);
        var set = new HashSet<int>(res.LakeCells);
        var geom = Vectorizer.Polygonize(dem, i => set.Contains(i), 1.0);
        return Ok(new
        {
            demSource = src.Description,
            synthetic = src.Synthetic,
            level = Math.Round(res.EstimatedLevel, 1),
            volumeMcm = Math.Round(res.EstimatedVolumeM3 / 1e6, 3),
            modelAreaKm2 = Math.Round(res.AreaKm2, 3),
            levelSource = res.LevelSource,
            notes = res.Notes,
            curve = res.Curve.Points.Select(pt => new { elevation = Math.Round(pt.Elevation, 1), areaKm2 = Math.Round(pt.AreaM2 / 1e6, 4), volumeMcm = Math.Round(pt.VolumeM3 / 1e6, 4) }),
            lake = geom == null ? null : JsonNode.Parse(GeoJsonOf(geom))
        });
    }

    public sealed record BreachRequest(string Type, double HeightM, double? WaterDepthM, double VolumeMcm, string Mode = "Overtopping",
        double CrestLengthM = 300, double CrestWidthM = 10, double StorageExponent = 2.5);

    /// <summary>Breach calculator: all empirical methods side by side, each routed through the reservoir.</summary>
    [HttpPost("breach")]
    public IActionResult Breach(BreachRequest req)
    {
        var type = ScenarioFactory.ParseType(req.Type);
        var mode = Enum.TryParse<FailureMode>(req.Mode, true, out var m) ? m : FailureMode.Overtopping;
        double hw = req.WaterDepthM ?? req.HeightM * 0.95;
        var input = new DamInput(type, req.HeightM, hw, req.VolumeMcm * 1e6, mode, req.CrestWidthM, req.CrestLengthM);
        var storage = StorageCurve.PowerLaw(0, hw, input.VolumeM3, req.StorageExponent);
        var methods = new[] { BreachMethod.Froehlich2008, BreachMethod.MacDonaldLangridgeMonopolis, BreachMethod.CostaSchuster };
        var results = methods.Select(bm =>
        {
            var b = BreachModels.Compute(input, bm);
            var h = BreachRouting.Route(storage, b, new BreachRouting.Options(req.HeightM, 0, hw, Dt: Math.Clamp(b.FormationTime / 500, 0.5, 5)));
            var d = h.Decimate(150);
            return new
            {
                method = b.Method, averageWidth = Math.Round(b.AverageWidth, 1), formationMin = Math.Round(b.FormationTime / 60, 1),
                empiricalPeak = b.EmpiricalPeak is double ep ? Math.Round(ep) : (double?)null, routedPeak = Math.Round(h.Peak),
                timeToPeakMin = Math.Round(h.TimeToPeak / 60, 1),
                series = d.Time.Zip(d.Discharge, (t, q) => new[] { Math.Round(t / 60, 1), Math.Round(q) })
            };
        }).ToList();
        return Ok(new
        {
            results,
            froehlich1995Peak = Math.Round(BreachModels.FroehlichPeak(input)),
            recommended = BreachModels.DefaultFor(type).ToString()
        });
    }

    public sealed record ThresholdRequest(Guid RunId, double Lon, double Lat, double Depth = 0.5, string? TargetName = null);

    /// <summary>"How much water has to be released before this bridge / camp / highway floods?"</summary>
    [HttpPost("inverse/threshold")]
    public async Task<IActionResult> Threshold(ThresholdRequest req, CancellationToken ct)
    {
        var o = _store.Get(req.RunId);
        if (o == null) return NotFound("run not found");
        var engine = new ScenarioEngine();
        var coarse = o.Prepared.Dem.Coarsen(2);
        if (!coarse.TryCell(new GeoPoint(req.Lon, req.Lat), out int r, out int c)) return BadRequest("target outside the run's model domain");
        int cell = coarse.Idx(r, c);
        double fullVolume = o.Prepared.Hydrograph.Volume();

        double DepthAt(double factor)
        {
            var res = engine.RunQuick(o.Prepared, factor, 2, ct: ct);
            // small neighbourhood so a target a few metres off the channel still registers
            double best = 0;
            for (int rr = r - 1; rr <= r + 1; rr++)
                for (int cc = c - 1; cc <= c + 1; cc++)
                    if (coarse.InBounds(rr, cc)) best = Math.Max(best, res.MaxDepth[coarse.Idx(rr, cc)]);
            return best;
        }

        var result = await Task.Run(() => InverseSolver.Threshold(DepthAt, req.Depth), ct);
        return Ok(new
        {
            target = req.TargetName ?? $"{req.Lat:F4}N {req.Lon:F4}E",
            requiredDepth = req.Depth,
            result.Found,
            releaseFraction = Math.Round(result.Factor, 3),
            releaseVolumeMcm = Math.Round(result.Factor * fullVolume / 1e6, 2),
            peakDischarge = Math.Round(result.Factor * o.Prepared.Hydrograph.Peak),
            depthAtThreshold = result.ValueAtFactor,
            result.Message,
            trace = result.Trace.Select(t => new { factor = Math.Round(t.Factor, 3), depth = Math.Round(t.Value, 2), t.Satisfied })
        });
    }

    public sealed record ObservedRequest(Guid RunId, JsonObject Observed);

    /// <summary>"Satellite shows this flood - how much was released upstream?" (best CSI over release scale)</summary>
    [HttpPost("inverse/observed")]
    public async Task<IActionResult> Observed(ObservedRequest req, CancellationToken ct)
    {
        var o = _store.Get(req.RunId);
        if (o == null) return NotFound("run not found");
        var coarse = o.Prepared.Dem.Coarsen(2);
        var obs = ValidationController.RasterizeGeoJson(coarse, req.Observed);
        if (!obs.Any(x => x)) return BadRequest("observed flood polygons do not overlap the model domain");
        var exclude = o.Prepared.Dem.ResampleTo(o.Prepared.ExcludeMask.Select(b => b ? 1f : 0f).ToArray(), coarse).Select(v => v > 0.5f).ToArray();
        var engine = new ScenarioEngine();
        double Csi(double factor)
        {
            var res = engine.RunQuick(o.Prepared, factor, 2, ct: ct);
            return ValidationMetrics.Compare(ValidationMetrics.WetMask(res.MaxDepth, 0.3), obs, exclude).Csi;
        }
        var result = await Task.Run(() => InverseSolver.BestFit(Csi, 0.05, 1.5, 8), ct);
        return Ok(new
        {
            releaseFraction = Math.Round(result.Factor, 3),
            releaseVolumeMcm = Math.Round(result.Factor * o.Prepared.Hydrograph.Volume() / 1e6, 2),
            peakDischarge = Math.Round(result.Factor * o.Prepared.Hydrograph.Peak),
            bestCsi = Math.Round(result.ValueAtFactor ?? 0, 3),
            result.Message,
            trace = result.Trace.Select(t => new { factor = Math.Round(t.Factor, 3), csi = Math.Round(t.Value, 3) })
        });
    }

    private static readonly System.Text.Json.JsonSerializerOptions GeoJsonOpts = new()
    {
        Converters = { new NetTopologySuite.IO.Converters.GeoJsonConverterFactory() }
    };

    internal static string GeoJsonOf(NetTopologySuite.Geometries.Geometry g) =>
        System.Text.Json.JsonSerializer.Serialize(g, GeoJsonOpts);
}
