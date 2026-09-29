using PralayDrishti.Application.Common.Models;
using PralayDrishti.API.Data;
using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Export;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.API.Services;

public sealed record RunSummary(
    Guid Id, string Name, string Status, double Progress, string Stage, DateTime CreatedUtc, DateTime? FinishedUtc,
    string Mode, string Origin, int? DamId, string? Error, KeyNumbers? Numbers);

public sealed record KeyNumbers(
    double FloodedAreaKm2, int PopulationAtRisk, int Settlements, double? FirstArrivalMin, double PeakDischarge,
    double MaxDepth, double LossCrore, int BridgesOvertopped, double? ReachKm, string DemSource, bool SyntheticDem);

public sealed record SeriesDto(double[] T, double[] V);

public sealed record GaugeDto(string Name, double Lon, double Lat, double[] T, double[] Depth, double[] Speed);

public sealed record SphGaugeDto(double DistanceM, double[] T, double[] Sph, double[] Swe, double[] Ritter, double SphArrival, double SweArrival,
    double SphPeak, double SwePeak);

public sealed record NearFieldDto(
    double Scale, int Particles, double WallSeconds, double HandoverDistance, double RitterDamQ, double SphDamQ,
    double? PeakForceKN, double? PeakPressureKPa, string? StructureName, double? StructureDistance,
    SeriesDto? Force, SeriesDto Handover, List<SphGaugeDto> Gauges, List<object> Frames, double[] BedX, double[] BedZ, List<string> Notes);

public sealed record RunDetail(
    RunSummary Summary,
    ScenarioSpec Spec,
    double[] Bounds,                    // west, south, east, north
    double[] Dam,                       // lon, lat
    object Breach,
    List<EmpiricalPeak> EmpiricalPeaks,
    SeriesDto Hydrograph,
    SeriesDto? RoutedHydrograph,
    SeriesDto? ReservoirLevel,
    ImpactReport Impact,
    List<GaugeDto> Gauges,
    NearFieldDto? NearField,
    object? Ensemble,
    List<CascadeEvent> Cascade,
    double[] SnapshotTimes,
    object Model,
    List<string> Log,
    object? NaturalDam,
    double[][] River,
    Dictionary<string, IReadOnlyList<(string label, string hex)>> Legends);

public static class RunViews
{
    public static RunSummary Summary(RunEntity r) => new(r.Id, r.Name, r.Status.ToString(), Math.Round(r.Progress, 3), r.Stage,
        r.CreatedUtc, r.FinishedUtc, r.Mode, r.Origin, r.DamId, r.Error,
        string.IsNullOrEmpty(r.SummaryJson) ? null : System.Text.Json.JsonSerializer.Deserialize<KeyNumbers>(r.SummaryJson, RunArchive.Json));

    public static KeyNumbers Numbers(ScenarioOutcome o, string demSource, bool synthetic) => new(
        o.Impact.FloodedAreaKm2, o.Impact.PopulationAtRisk, o.Impact.SettlementsAffected,
        o.Impact.Places.Where(p => p.ArrivalMin != null).Select(p => p.ArrivalMin).DefaultIfEmpty(null).Min(),
        Math.Round(o.Prepared.Hydrograph.Peak), o.Impact.MaxDepth, Math.Round(o.Impact.Loss.TotalCrore, 1),
        o.Impact.Bridges.Count(b => b.Overtopped), o.Impact.FloodFrontReachKm, demSource, synthetic);

    public static RunDetail Detail(RunEntity r, ScenarioOutcome o)
    {
        var p = o.Prepared;
        var g = o.FarField.Dem;
        var hyd = p.Hydrograph.Decimate(400);
        var routed = p.RoutedHydrograph.Decimate(400);
        var levels = routed.Level.Select((l, i) => (t: routed.Time[i], l)).Where(x => !double.IsNaN(x.l)).ToList();

        var gauges = o.FarField.Gauges.Select(ga =>
        {
            var pt = g.CellCenter(ga.Cell);
            int step = Math.Max(1, ga.Time.Count / 300);
            return new GaugeDto(ga.Name, pt.Lon, pt.Lat,
                ga.Time.Where((_, i) => i % step == 0).Select(x => (double)x).ToArray(),
                ga.Depth.Where((_, i) => i % step == 0).Select(x => Math.Round(x, 3)).ToArray(),
                ga.Speed.Where((_, i) => i % step == 0).Select(x => Math.Round(x, 3)).ToArray());
        }).ToList();

        NearFieldDto? nf = null;
        if (o.NearField is { } n)
        {
            int fstep = Math.Max(1, n.Frames.Count / 60);
            nf = new NearFieldDto(n.Scale, n.Particles, n.SphWallSeconds, n.HandoverDistance, n.RitterDamUnitDischarge,
                n.SphDamUnitDischargePeak, n.PeakImpactForceKN, n.PeakImpactPressureKPa, n.StructureName, n.StructureDistance,
                n.ForceKN.Count > 0 ? new SeriesDto(n.ForceTime.ToArray(), n.ForceKN.Select(f => Math.Round(f, 1)).ToArray()) : null,
                new SeriesDto(n.HandoverHydrograph.Time.ToArray(), n.HandoverHydrograph.Discharge.Select(q => Math.Round(q)).ToArray()),
                n.Gauges.Select(gc => new SphGaugeDto(gc.DistanceM, gc.Time.Select(t => Math.Round(t, 2)).ToArray(),
                    gc.SphDepth.Select(d => Math.Round(d, 2)).ToArray(), gc.SweDepth.Select(d => Math.Round(d, 2)).ToArray(),
                    gc.RitterDepth.Select(d => Math.Round(d, 2)).ToArray(), gc.SphArrival, gc.SweArrival, gc.SphPeakDepth, gc.SwePeakDepth)).ToList(),
                n.Frames.Where((_, i) => i % fstep == 0).Select(f => (object)new
                {
                    t = Math.Round(f.Time, 2),
                    x = f.X.Select(v => Math.Round(v, 1)).ToArray(),
                    z = f.Z.Select(v => Math.Round(v, 1)).ToArray()
                }).ToList(),
                n.BedX, n.BedZ, n.Notes);
        }

        object? ens = o.Ensemble is { } e ? new
        {
            e.Members,
            e.Threshold,
            areas = e.FloodedAreaKm2.Select(a => Math.Round(a, 3)).ToList(),
            samples = e.Samples,
            places = o.Impact.Places.Select(pl =>
            {
                var ci = g.CellIndex(new(pl.Lon, pl.Lat));
                return new { pl.Name, probability = ci is int i ? Math.Round(e.Probability[i], 2) : 0, p90 = ci is int j ? Math.Round(e.P90Depth[j], 2) : 0 };
            }).ToList()
        } : null;

        object? nat = p.NaturalDam is { } nd ? new
        {
            nd.CrestElevation, nd.DamHeight, nd.LakeAreaM2, nd.LakeVolumeM3, nd.MaxLakeDepth, nd.Notes,
            curve = nd.Curve.Points.Select(sp => new { sp.Elevation, sp.AreaM2, sp.VolumeM3 })
        } : null;

        return new RunDetail(
            Summary(r), p.Spec, new[] { g.West, g.South, g.East, g.North }, new[] { p.DamPoint.Lon, p.DamPoint.Lat },
            new { p.Breach.Method, p.Breach.AverageWidth, p.Breach.BottomWidth, p.Breach.Depth, p.Breach.SideSlope, FormationTimeMin = p.Breach.FormationTime / 60, p.Breach.EmpiricalPeak },
            p.EmpiricalPeaks,
            new SeriesDto(hyd.Time.ToArray(), hyd.Discharge.Select(q => Math.Round(q, 1)).ToArray()),
            new SeriesDto(routed.Time.ToArray(), routed.Discharge.Select(q => Math.Round(q, 1)).ToArray()),
            levels.Count > 0 ? new SeriesDto(levels.Select(x => x.t).ToArray(), levels.Select(x => Math.Round(x.l, 2)).ToArray()) : null,
            o.Impact, gauges, nf, ens, o.Cascade,
            o.FarField.Snapshots.Select(s => Math.Round(s.Time)).ToArray(),
            new
            {
                engine = o.EngineName, cols = g.Cols, rows = g.Rows, dx = Math.Round(g.Dx, 1), dy = Math.Round(g.Dy, 1),
                steps = o.FarField.Steps, wallSec = Math.Round(o.FarField.WallTime.TotalSeconds, 1),
                massError = o.FarField.MassError, inflowMcm = o.FarField.InflowVolume / 1e6, timings = o.TimingsSec
            },
            p.Log, nat,
            p.DownstreamPath.Where((_, i) => i % 3 == 0).Select(i => { var c = g.CellCenter(i); return new[] { Math.Round(c.Lon, 5), Math.Round(c.Lat, 5) }; }).ToArray(),
            new Dictionary<string, IReadOnlyList<(string, string)>>
            {
                ["depth"] = ColorMaps.Legend(LayerKind.Depth),
                ["hazard"] = ColorMaps.Legend(LayerKind.Hazard),
                ["arrival"] = ColorMaps.Legend(LayerKind.Arrival),
                ["velocity"] = ColorMaps.Legend(LayerKind.Velocity),
                ["probability"] = ColorMaps.Legend(LayerKind.Probability)
            });
    }
}
