using System.Text.Json;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Export;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Hydraulics;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.Infrastructure.Services;

/// <summary>Stand-alone hydraulic tools: natural-dam lake, satellite reservoir, breach calculator, inverse queries, NDWI.</summary>
public sealed class HydraulicAnalysis : IHydraulicAnalysis
{
    private readonly DemProvider _dem;
    private readonly ResultStore _store;
    private readonly AppOptions _opt;

    public HydraulicAnalysis(DemProvider dem, ResultStore store, AppOptions opt)
    {
        _dem = dem;
        _store = store;
        _opt = opt;
    }

    private static JsonPayload Payload(object o) => new(JsonSerializer.Serialize(o, ResultStore.Json));

    public async Task<JsonPayload> EstimateNaturalDamAsync(List<double[]> polygon, double? crest, double? heightAboveBed,
        double? currentLevel, DamType type, CancellationToken ct)
    {
        var ring = polygon.Select(p => new GeoPoint(p[0], p[1])).ToList();
        var center = new GeoPoint(ring.Average(p => p.Lon), ring.Average(p => p.Lat));
        var src = await _dem.ForAreaAsync(center, 6, 12, _opt.DefaultDemZoom, ct);
        var dem = src.Dem.Coarsen(src.Dem.SuggestFactor(400_000));
        NaturalDamResult res;
        try
        {
            res = await Task.Run(() => NaturalDamAnalyzer.Analyze(dem, new NaturalDamAnalyzer.Request(ring, crest, heightAboveBed, currentLevel)), ct);
        }
        catch (ArgumentException ex)
        {
            throw new BusinessRuleException(ex.Message);
        }

        double level = res.CurrentLevel is double cl ? Math.Min(cl, res.CrestElevation) : res.CrestElevation;
        double vol = res.CurrentVolumeM3 ?? res.LakeVolumeM3;
        var input = new DamInput(type, res.DamHeight, level - res.BedElevation, Math.Max(vol, 1), FailureMode.Overtopping, 50, 200);
        var breach = BreachModels.LandslideDam(input);
        var hyd = BreachRouting.Route(res.Curve, breach, new BreachRouting.Options(res.CrestElevation, res.BedElevation, level,
            Dt: Math.Clamp(breach.FormationTime / 500, 0.5, 5)));
        var thin = hyd.Decimate(200);
        var lakeSet = new HashSet<int>(res.LakeCells);

        return Payload(new
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
            hydrograph = thin.Time.Zip(thin.Discharge, (t, q) => new[] { Math.Round(t / 60, 1), Math.Round(q) }),
            lake = GeoJsonMask.ToNode(Vectorizer.Polygonize(dem, i => lakeSet.Contains(i), 1.0))
        });
    }

    public async Task<JsonPayload> EstimateReservoirAsync(double lon, double lat, double? areaKm2, double? altimetryLevel,
        double deadStorageMcm, double maxRise, CancellationToken ct)
    {
        var p = new GeoPoint(lon, lat);
        double r = areaKm2 is double a ? Math.Clamp(Math.Sqrt(a) * 2.5, 6, 40) : 15;
        var src = await _dem.ForAreaAsync(p, r, r, _opt.DefaultDemZoom, ct);
        var dem = src.Dem.Coarsen(src.Dem.SuggestFactor(500_000));
        SatelliteReservoirEstimator.Result res;
        try
        {
            res = await Task.Run(() => SatelliteReservoirEstimator.Estimate(dem,
                new SatelliteReservoirEstimator.Request(p, areaKm2, altimetryLevel, maxRise, deadStorageMcm * 1e6)), ct);
        }
        catch (ArgumentException ex)
        {
            throw new BusinessRuleException(ex.Message);
        }
        var set = new HashSet<int>(res.LakeCells);
        return Payload(new
        {
            demSource = src.Description,
            synthetic = src.Synthetic,
            level = Math.Round(res.EstimatedLevel, 1),
            volumeMcm = Math.Round(res.EstimatedVolumeM3 / 1e6, 3),
            modelAreaKm2 = Math.Round(res.AreaKm2, 3),
            levelSource = res.LevelSource,
            notes = res.Notes,
            curve = res.Curve.Points.Select(pt => new { elevation = Math.Round(pt.Elevation, 1), areaKm2 = Math.Round(pt.AreaM2 / 1e6, 4), volumeMcm = Math.Round(pt.VolumeM3 / 1e6, 4) }),
            lake = GeoJsonMask.ToNode(Vectorizer.Polygonize(dem, i => set.Contains(i), 1.0))
        });
    }

    public JsonPayload CalculateBreach(DamType type, double heightM, double? waterDepthM, double volumeMcm, FailureMode mode,
        double crestLengthM, double crestWidthM, double storageExponent)
    {
        double hw = waterDepthM ?? heightM * 0.95;
        var input = new DamInput(type, heightM, hw, volumeMcm * 1e6, mode, crestWidthM, crestLengthM);
        var storage = StorageCurve.PowerLaw(0, hw, input.VolumeM3, storageExponent);
        var methods = new[] { BreachMethod.Froehlich2008, BreachMethod.MacDonaldLangridgeMonopolis, BreachMethod.CostaSchuster };
        var results = methods.Select(bm =>
        {
            var b = BreachModels.Compute(input, bm);
            var h = BreachRouting.Route(storage, b, new BreachRouting.Options(heightM, 0, hw, Dt: Math.Clamp(b.FormationTime / 500, 0.5, 5)));
            var d = h.Decimate(150);
            return new
            {
                method = b.Method,
                averageWidth = Math.Round(b.AverageWidth, 1),
                formationMin = Math.Round(b.FormationTime / 60, 1),
                empiricalPeak = b.EmpiricalPeak is double ep ? Math.Round(ep) : (double?)null,
                routedPeak = Math.Round(h.Peak),
                timeToPeakMin = Math.Round(h.TimeToPeak / 60, 1),
                series = d.Time.Zip(d.Discharge, (t, q) => new[] { Math.Round(t / 60, 1), Math.Round(q) })
            };
        }).ToList();
        return Payload(new { results, froehlich1995Peak = Math.Round(BreachModels.FroehlichPeak(input)), recommended = BreachModels.DefaultFor(type).ToString() });
    }

    public async Task<JsonPayload> InverseThresholdAsync(Guid runId, double lon, double lat, double depth, string? targetName, CancellationToken ct)
    {
        var o = _store.Get(runId) ?? throw new NotFoundException("Run results", runId);
        var engine = new ScenarioEngine();
        var coarse = o.Prepared.Dem.Coarsen(2);
        if (!coarse.TryCell(new GeoPoint(lon, lat), out int r, out int c))
            throw new BusinessRuleException("Target is outside the run's model domain.");
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

        var result = await Task.Run(() => InverseSolver.Threshold(DepthAt, depth), ct);
        return Payload(new
        {
            target = targetName ?? $"{lat:F4}N {lon:F4}E",
            requiredDepth = depth,
            result.Found,
            releaseFraction = Math.Round(result.Factor, 3),
            releaseVolumeMcm = Math.Round(result.Factor * fullVolume / 1e6, 2),
            peakDischarge = Math.Round(result.Factor * o.Prepared.Hydrograph.Peak),
            depthAtThreshold = result.ValueAtFactor,
            result.Message,
            trace = result.Trace.Select(t => new { factor = Math.Round(t.Factor, 3), depth = Math.Round(t.Value, 2), t.Satisfied })
        });
    }

    public async Task<JsonPayload> InverseFromObservedAsync(Guid runId, string observedGeoJson, CancellationToken ct)
    {
        var o = _store.Get(runId) ?? throw new NotFoundException("Run results", runId);
        var coarse = o.Prepared.Dem.Coarsen(2);
        var obs = GeoJsonMask.Rasterize(coarse, observedGeoJson);
        if (!obs.Any(x => x)) throw new BusinessRuleException("Observed flood polygons do not overlap the model domain.");
        var exclude = o.Prepared.Dem.ResampleTo(o.Prepared.ExcludeMask.Select(b => b ? 1f : 0f).ToArray(), coarse).Select(v => v > 0.5f).ToArray();
        var engine = new ScenarioEngine();

        double Csi(double factor)
        {
            var res = engine.RunQuick(o.Prepared, factor, 2, ct: ct);
            return ValidationMetrics.Compare(ValidationMetrics.WetMask(res.MaxDepth, 0.3), obs, exclude).Csi;
        }

        var result = await Task.Run(() => InverseSolver.BestFit(Csi, 0.05, 1.5, 8), ct);
        return Payload(new
        {
            releaseFraction = Math.Round(result.Factor, 3),
            releaseVolumeMcm = Math.Round(result.Factor * o.Prepared.Hydrograph.Volume() / 1e6, 2),
            peakDischarge = Math.Round(result.Factor * o.Prepared.Hydrograph.Peak),
            bestCsi = Math.Round(result.ValueAtFactor ?? 0, 3),
            result.Message,
            trace = result.Trace.Select(t => new { factor = Math.Round(t.Factor, 3), csi = Math.Round(t.Value, 3) })
        });
    }

    public Task<IReadOnlyList<LakeDetectionInput>> DetectNewLakesAsync(string beforePath, string afterPath, double threshold,
        double minAreaKm2, CancellationToken ct) => Task.Run<IReadOnlyList<LakeDetectionInput>>(() =>
    {
        var gb = ReadRaster(beforePath);
        var ga = ReadRaster(afterPath);
        if (gb.Cols != ga.Cols || gb.Rows != ga.Rows) throw new BusinessRuleException("Before/after rasters must be on the same grid.");
        var found = LakeChangeDetector.Detect(ga, gb.Z.Select(v => v > threshold).ToArray(), ga.Z.Select(v => v > threshold).ToArray(), minAreaKm2);
        return found.Take(20).Select(f => new LakeDetectionInput(f.Centroid.Lat, f.Centroid.Lon, Math.Round(f.AreaKm2, 4), 0, null,
            "Uploaded NDWI change detection")).ToList();
    }, ct);

    public Task<double> WaterAreaKm2Async(string ndwiPath, double lon, double lat, double threshold, CancellationToken ct) => Task.Run(() =>
    {
        var g = ReadRaster(ndwiPath);
        return Math.Round(SatelliteReservoirEstimator.WaterBodyAreaKm2(g, g.Z.Select(v => v > threshold).ToArray(), new GeoPoint(lon, lat)), 4);
    }, ct);

    private static DemGrid ReadRaster(string path)
    {
        try
        {
            return GeoTiffReader.Read(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            throw new BusinessRuleException($"Could not read raster: {ex.Message}");
        }
    }
}
