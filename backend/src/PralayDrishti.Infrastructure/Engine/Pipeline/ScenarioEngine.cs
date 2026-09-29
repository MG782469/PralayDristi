using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using System.Diagnostics;
using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Hydraulics;
using PralayDrishti.Infrastructure.Engine.Solvers;

namespace PralayDrishti.Infrastructure.Engine.Pipeline;

public sealed record StageProgress(string Stage, double Fraction);

/// <summary>
/// The end-to-end chain: DEM -> river -> breach -> (SPH near field) -> 2D far field -> impact -> ensemble.
/// The API calls this from a background worker; tests call it directly on synthetic valleys.
/// </summary>
public sealed class ScenarioEngine
{
    private readonly IFarFieldRunner _farField;

    public ScenarioEngine(IFarFieldRunner? farField = null) => _farField = farField ?? new InternalSweRunner();

    /// <summary>
    /// Bounding box of the river corridor below (and a bit above) the dam, traced on a coarse DEM.
    /// Lets us fetch the fine DEM only where it matters instead of a 80 x 80 km square.
    /// </summary>
    public static BBox CorridorBounds(DemGrid coarse, GeoPoint dam, double downstreamKm, double upstreamKm, double bufferKm = 3)
    {
        var routing = Hydrology.Route(coarse);
        int cell = Hydrology.SnapToRiver(coarse, routing.Accumulation, dam, 1500);
        var down = Hydrology.TraceDownstream(coarse, routing.Receivers, cell, downstreamKm * 1000);
        var up = Hydrology.TraceUpstream(coarse, routing.Receivers, routing.Accumulation, cell, upstreamKm * 1000);
        var pts = down.Concat(up).Select(i => coarse.CellCenter(i)).Append(dam).ToList();
        var bb = BBox.FromPoints(pts);
        double buf = bufferKm / 111.0;
        var b = bb.Expand(buf);
        // intersect with what we have
        return new BBox(Math.Max(b.West, coarse.West), Math.Max(b.South, coarse.South),
                        Math.Min(b.East, coarse.East), Math.Min(b.North, coarse.North));
    }

    public PreparedScenario Prepare(ScenarioSpec spec, DemGrid rawDem)
    {
        var log = new List<string>();
        int factor = rawDem.SuggestFactor(spec.MaxCells);
        var dem = factor > 1 ? rawDem.Coarsen(factor) : rawDem.Clone();
        var original = dem.Clone();
        log.Add($"Grid {dem.Cols} x {dem.Rows} cells, {dem.Dx:F0} x {dem.Dy:F0} m (coarsen factor {factor}).");

        var routing = Hydrology.Route(dem);
        NaturalDamResult? nat = null;
        bool[] exclude = new bool[dem.Count];
        int damCell;
        double bed, crest;
        StorageCurve storage;
        BreachGeometry breach;
        var peaks = new List<EmpiricalPeak>();
        double hw, volume;
        var damSpec = spec.Dam;

        if (spec.Mode == ScenarioMode.NaturalDam)
        {
            if (spec.NaturalDam is null || spec.NaturalDam.DebrisPolygon.Count < 3)
                throw new ArgumentException("natural dam mode needs a debris polygon");
            nat = NaturalDamAnalyzer.Analyze(dem, new NaturalDamAnalyzer.Request(
                spec.NaturalDam.Ring(), spec.NaturalDam.CrestElevation, spec.NaturalDam.HeightAboveBed,
                spec.NaturalDam.CurrentLakeLevel));
            log.AddRange(nat.Notes);
            damCell = nat.BlockageRiverCell;
            bed = nat.BedElevation;
            crest = nat.CrestElevation;
            storage = nat.Curve;
            double level = nat.CurrentLevel is double cl ? Math.Min(cl, crest) : crest;
            hw = level - bed;
            volume = storage.VolumeAt(level);
            foreach (int i in nat.LakeCells) exclude[i] = true;
            for (int i = 0; i < dem.Count; i++)
                if (nat.DebrisMask[i])
                {
                    exclude[i] = true;
                    dem.Z[i] = (float)Math.Max(dem.Z[i], crest);
                }
            var dInput = new DamInput(spec.NaturalDam.Type, crest - bed, hw, volume, FailureMode.Overtopping,
                CrestWidthM: 50, CrestLengthM: Math.Max(100, Math.Sqrt(nat.DebrisMask.Count(m => m) * dem.CellArea)));
            breach = BreachModels.LandslideDam(dInput);
            peaks.Add(new("Costa & Schuster (1988)", breach.EmpiricalPeak ?? 0));
            peaks.Add(new("Froehlich (1995)", BreachModels.FroehlichPeak(dInput)));
            peaks.Add(new("MacDonald & Langridge-Monopolis (1984)", BreachModels.MacDonaldLangridgeMonopolis(dInput).EmpiricalPeak ?? 0));
            log.Add($"Natural lake: spill {crest:F1} m, height {crest - bed:F1} m, volume {volume / 1e6:F2} MCM.");
        }
        else
        {
            damCell = Hydrology.SnapToRiver(dem, routing.Accumulation, damSpec.Location(), 1000);
            bed = dem.Z[damCell];
            crest = bed + damSpec.HeightM;
            hw = damSpec.HwOrDefault;
            volume = damSpec.ReservoirVolumeM3;
            storage = StorageCurve.PowerLaw(bed, hw, volume, damSpec.StorageExponent);
            var input = new DamInput(damSpec.Type, damSpec.HeightM, hw, volume, damSpec.FailureMode,
                damSpec.CrestWidthM, damSpec.CrestLengthM);
            var method = damSpec.BreachMethod ?? BreachModels.DefaultFor(damSpec.Type);
            breach = BreachModels.Compute(input, method);
            if (damSpec.BreachWidthM is double bw) breach = breach with { AverageWidth = bw, BottomWidth = Math.Max(1, bw - breach.SideSlope * breach.Depth), Method = breach.Method + " (width override)" };
            if (damSpec.FormationTimeMin is double tfm) breach = breach with { FormationTime = tfm * 60, Method = breach.Method + " (time override)" };
            peaks.Add(new("Froehlich (1995)", BreachModels.FroehlichPeak(input)));
            peaks.Add(new("MacDonald & Langridge-Monopolis (1984)", BreachModels.MacDonaldLangridgeMonopolis(input).EmpiricalPeak ?? 0));
            if (damSpec.Type is DamType.LandslideDam or DamType.MoraineDam)
                peaks.Add(new("Costa & Schuster (1988)", BreachModels.LandslideDam(input).EmpiricalPeak ?? 0));
            log.Add($"{damSpec.Name}: snapped to river at {dem.CellCenter(damCell)}, bed {bed:F0} m, crest {crest:F0} m.");
        }

        var down = Hydrology.TraceDownstream(dem, routing.Receivers, damCell, spec.DownstreamKm * 1000);
        var up = Hydrology.TraceUpstream(dem, routing.Receivers, routing.Accumulation, damCell, spec.UpstreamKm * 1000);
        var dir = Hydrology.FlowVector(dem, down);

        // dam body as a wall across the valley so injected water can't run back up into the reservoir area
        var damLine = CrossValleyLine(dem, damCell, dir, crest, 3000);
        if (spec.Mode != ScenarioMode.NaturalDam)
            foreach (int i in damLine) { dem.Z[i] = (float)Math.Max(dem.Z[i], crest); exclude[i] = true; }

        // no bathymetry: burn a nominal channel below the dam, then carve out DEM sills along the river
        var channel = Hydrology.FourConnected(dem, down.Skip(1).ToList());
        if (spec.ChannelBurnM > 0)
            Hydrology.BurnChannel(dem, channel.Skip(1), (float)spec.ChannelBurnM);
        int carved = Hydrology.CarveMonotonic(dem, channel);
        if (carved > 0) log.Add($"Carved {carved} DEM sill cells along the river so the channel stays connected.");

        var manning = new float[dem.Count];
        Array.Fill(manning, (float)spec.Manning);
        foreach (int i in channel) manning[i] = (float)spec.ChannelManning;

        var routed = BreachRouting.Route(storage, breach, new BreachRouting.Options(
            CrestElevation: crest, InvertElevation: bed, InitialLevel: bed + hw,
            Dt: Math.Clamp(breach.FormationTime / 500, 0.5, 5)));
        log.Add($"Breach: {breach.Method}, B = {breach.AverageWidth:F0} m, tf = {breach.FormationTime / 60:F0} min, Qp = {routed.Peak:F0} m3/s.");

        int outlet = down.Count > 1 ? down[1] : damCell;
        if (nat != null) outlet = down.FirstOrDefault(i => !nat.DebrisMask[i], outlet);
        var inflowCells = InflowCells(dem, outlet, dir, breach.AverageWidth, crest);

        // cascade: downstream dams live inside the grid
        var structures = new List<DamStructure>();
        float[]? init = null;
        if (spec.Mode == ScenarioMode.Cascade && spec.DownstreamDams.Count > 0)
        {
            init = new float[dem.Count];
            foreach (var d in spec.DownstreamDams)
            {
                var st = BuildDownstreamDam(dem, d, down, dir, init, exclude, log);
                if (st != null) structures.Add(st);
            }
        }

        return new PreparedScenario
        {
            Spec = spec,
            Dem = dem,
            OriginalDem = original,
            DamPoint = dem.CellCenter(damCell),
            DamCell = damCell,
            DownstreamPath = down,
            UpstreamPath = up,
            InflowCells = inflowCells,
            FlowDir = dir,
            Breach = breach,
            Hydrograph = routed,
            RoutedHydrograph = routed,
            Storage = storage,
            BedAtDam = bed,
            ManningField = manning,
            ExcludeMask = exclude,
            EmpiricalPeaks = peaks,
            NaturalDam = nat,
            Structures = structures,
            InitialDepth = init,
            Log = log
        };
    }

    public ScenarioOutcome Run(ScenarioSpec spec, DemGrid rawDem, ExposureSet exposure,
        IProgress<StageProgress>? progress = null, CancellationToken ct = default)
    {
        var timings = new Dictionary<string, double>();
        var sw = Stopwatch.StartNew();
        progress?.Report(new("Preparing DEM and breach", 0));
        var prep = Prepare(spec, rawDem);
        timings["prepare"] = sw.Elapsed.TotalSeconds;

        NearFieldResult? near = null;
        if (spec.RunSph)
        {
            sw.Restart();
            progress?.Report(new("SPH near field", 0));
            near = RunNearField(prep, spec, new Progress<double>(p => progress?.Report(new("SPH near field", p))), ct);
            timings["sph"] = sw.Elapsed.TotalSeconds;
            if (spec.SphHandover && near != null)
            {
                prep.Hydrograph = BreachRouting.Blend(near.HandoverHydrograph, prep.RoutedHydrograph, near.HandoverTime);
                prep.Log.Add($"Inflow = SPH handover for first {near.HandoverTime:F0} s, then reservoir routing.");
            }
        }

        sw.Restart();
        progress?.Report(new("2D far field", 0));
        var setup = BuildSetup(prep, 1.0, keepSnapshots: true, exposure);
        var far = _farField.Run(setup, new Progress<double>(p => progress?.Report(new("2D far field", p))), ct);
        timings["farfield"] = sw.Elapsed.TotalSeconds;
        prep.Log.Add($"Far field: {far.Steps} steps, {far.WallTime.TotalSeconds:F1} s wall, mass error {far.MassError:P3}.");

        sw.Restart();
        progress?.Report(new("Impact analysis", 0));
        var impact = ImpactAnalyzer.Analyze(far, exposure, prep.DamPoint, prep.DownstreamPath, prep.ExcludeMask,
            new ImpactOptions { WarningDisseminationMin = spec.WarningMinutes });
        var hazard = Hazard.ClassifyGrid(far.MaxDepth, far.MaxSpeed);
        for (int i = 0; i < hazard.Length; i++) if (prep.ExcludeMask[i]) hazard[i] = 0;
        timings["impact"] = sw.Elapsed.TotalSeconds;

        EnsembleResult? ens = null;
        if (spec.EnsembleMembers > 1)
        {
            sw.Restart();
            ens = RunEnsemble(prep, spec, progress, ct);
            timings["ensemble"] = sw.Elapsed.TotalSeconds;
        }

        // the solver works on fresh copies of the structures, so failures come back through the result
        var cascade = prep.Structures.Select(s =>
        {
            var hit = far.Failures.FirstOrDefault(f => f.name == s.Name);
            return new CascadeEvent(s.Name, hit.name is null ? null : Math.Round(hit.time / 60, 1), 0, s.Crest);
        }).ToList();
        progress?.Report(new("Done", 1));

        return new ScenarioOutcome
        {
            Prepared = prep,
            FarField = far,
            Impact = impact,
            HazardClass = hazard,
            NearField = near,
            Ensemble = ens,
            Cascade = cascade,
            EngineName = _farField.Name,
            TimingsSec = timings
        };
    }

    public SweSetup BuildSetup(PreparedScenario prep, double releaseFactor, bool keepSnapshots,
        ExposureSet? exposure = null, double manningFactor = 1.0, Hydrograph? hydrographOverride = null, DemGrid? onGrid = null)
    {
        var spec = prep.Spec;
        var gauges = new List<Gauge>();
        var dem = onGrid ?? prep.Dem;
        if (exposure != null && onGrid == null)
        {
            foreach (var p in exposure.Points.Where(p => p.Kind is AssetKind.Village or AssetKind.Town or AssetKind.Bridge or AssetKind.Barrage or AssetKind.PowerHouse))
                if (dem.CellIndex(p.Location) is int ci) gauges.Add(new Gauge { Name = p.Name, Cell = ci });
            if (prep.DownstreamPath.Count > 3)
                gauges.Insert(0, new Gauge { Name = "Dam toe", Cell = prep.DownstreamPath[Math.Min(3, prep.DownstreamPath.Count - 1)] });
        }

        var hyd = hydrographOverride ?? prep.Hydrograph;
        if (Math.Abs(releaseFactor - 1) > 1e-9) hyd = hyd.Scaled(releaseFactor);

        int[] inflow = prep.InflowCells;
        float[] manning = prep.ManningField;
        float[]? init = prep.InitialDepth;
        var structures = prep.Structures;
        if (onGrid != null)
        {
            // map onto a coarser grid for fast runs
            inflow = prep.InflowCells.Select(i => onGrid.CellIndex(prep.Dem.CellCenter(i))).OfType<int>().Distinct().ToArray();
            manning = prep.Dem.ResampleTo(prep.ManningField, onGrid);
            init = prep.InitialDepth == null ? null : prep.Dem.ResampleTo(prep.InitialDepth, onGrid);
            structures = new List<DamStructure>();
        }
        else if (structures.Count > 0)
        {
            // fresh copies so every run starts un-failed
            structures = structures.Select(s => new DamStructure
            {
                Name = s.Name, Cells = s.Cells, BreachCells = s.BreachCells, OriginalBed = s.OriginalBed, Crest = s.Crest,
                Breach = s.Breach, ProbeCells = s.ProbeCells, OvertopTrigger = s.OvertopTrigger, ForcedFailureTime = s.ForcedFailureTime
            }).ToList();
        }
        if (Math.Abs(manningFactor - 1) > 1e-9) manning = manning.Select(m => (float)(m * manningFactor)).ToArray();

        return new SweSetup
        {
            Dem = dem,
            ManningField = manning,
            Inflows = { new InflowSource { Cells = inflow, Hydrograph = hyd, DirEast = prep.FlowDir.east, DirNorth = prep.FlowDir.north } },
            Structures = structures,
            Gauges = gauges,
            InitialDepth = init,
            Duration = spec.DurationHours * 3600,
            SnapshotInterval = Math.Max(120, spec.DurationHours * 3600 / 48),
            GaugeInterval = 30,
            ArrivalDepth = spec.ArrivalDepth,
            KeepSnapshots = keepSnapshots
        };
    }

    /// <summary>Fast far-field evaluation on a coarsened grid - used by inverse queries and ensembles.</summary>
    public SweResult RunQuick(PreparedScenario prep, double releaseFactor, int coarsen = 2, double manningFactor = 1.0,
        Hydrograph? hydrograph = null, CancellationToken ct = default)
    {
        var grid = coarsen > 1 ? prep.Dem.Coarsen(coarsen) : prep.Dem;
        var setup = BuildSetup(prep, releaseFactor, keepSnapshots: false, null, manningFactor, hydrograph, coarsen > 1 ? grid : null);
        return new SweSolver(setup).Run(null, ct);
    }

    private NearFieldResult? RunNearField(PreparedScenario prep, ScenarioSpec spec, IProgress<double> progress, CancellationToken ct)
    {
        // profile from the unconditioned DEM - the conditioned one has the dam wall burnt in
        var dem = prep.OriginalDem;
        var path = prep.UpstreamPath.Concat(prep.DownstreamPath.Skip(1)).ToList();
        if (path.Count < 6) return null;
        var (ch, bed) = Hydrology.Profile(dem, path);
        bed[prep.UpstreamPath.Count - 1] = prep.BedAtDam;
        double offset = ch[prep.UpstreamPath.Count - 1];
        var chain = ch.Select(c => c - offset).ToArray();
        double H = prep.Hydrograph.Level.Where(l => !double.IsNaN(l)).DefaultIfEmpty(prep.BedAtDam + 10).First() - prep.BedAtDam;
        if (H < 2) return null;

        return HybridCoupler.Run(new NearFieldRequest
        {
            Chainage = chain,
            Bed = bed,
            DamHeight = H,
            BreachWidth = prep.Breach.AverageWidth,
            StructureDistance = spec.Structure?.DistanceKm * 1000,
            StructureHeight = spec.Structure?.HeightM ?? 15,
            StructureWidth = spec.Structure?.WidthM ?? 50,
            StructureName = spec.Structure?.Name ?? "structure",
            DebrisFlow = spec.DebrisFlow,
            Resolution = spec.SphResolution,
            Manning = spec.ChannelManning
        }, progress, ct);
    }

    private EnsembleResult RunEnsemble(PreparedScenario prep, ScenarioSpec spec, IProgress<StageProgress>? progress, CancellationToken ct)
    {
        var members = Ensemble.Sample(spec.EnsembleMembers, spec.EnsembleSeed, spec.Mode == ScenarioMode.NaturalDam);
        var depths = new List<float[]>();
        var coarse = prep.Dem.Coarsen(2);
        for (int k = 0; k < members.Count; k++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new($"Ensemble member {k + 1}/{members.Count}", (double)k / members.Count));
            var m = members[k];
            var breach = BreachModels.Scale(prep.Breach, m.WidthFactor, m.TimeFactor);
            var storage = m.VolumeFactor == 1.0 ? prep.Storage : new StorageCurve(prep.Storage.Points
                .Select(p => new StoragePoint(p.Elevation, p.AreaM2 * m.VolumeFactor, p.VolumeM3 * m.VolumeFactor)).ToList());
            double crest = prep.BedAtDam + prep.Breach.Depth;
            double level = prep.RoutedHydrograph.Level.FirstOrDefault(l => !double.IsNaN(l), crest);
            var hyd = BreachRouting.Route(storage, breach, new BreachRouting.Options(crest, prep.BedAtDam, level,
                Dt: Math.Clamp(breach.FormationTime / 500, 0.5, 5)));
            var res = RunQuick(prep, 1.0, 2, m.ManningFactor, hyd, ct);
            var d = coarse.ResampleTo(res.MaxDepth, prep.Dem);
            for (int i = 0; i < d.Length; i++) if (prep.ExcludeMask[i]) d[i] = 0;
            depths.Add(d);
        }
        return Ensemble.Aggregate(depths, prep.Dem.CellArea, 0.3, members);
    }

    private static int[] InflowCells(DemGrid dem, int outlet, (double east, double north) dir, double width, double crest)
    {
        int r0 = outlet / dem.Cols, c0 = outlet % dem.Cols;
        double pe = -dir.north, pn = dir.east;          // perpendicular
        int half = Math.Clamp((int)Math.Round(width / Math.Min(dem.Dx, dem.Dy) / 2), 0, 4);
        var cells = new List<int> { outlet };
        for (int k = -half; k <= half; k++)
        {
            if (k == 0) continue;
            int c = c0 + (int)Math.Round(k * pe);
            int r = r0 - (int)Math.Round(k * pn);
            if (!dem.InBounds(r, c)) continue;
            int i = dem.Idx(r, c);
            if (dem.Z[i] < crest && !cells.Contains(i)) cells.Add(i);
        }
        return cells.ToArray();
    }

    /// <summary>Line across the valley through a cell, extended until the ground rises above the given level.</summary>
    private static List<int> CrossValleyLine(DemGrid dem, int cell, (double east, double north) dir, double level, double maxHalfLength)
    {
        int r0 = cell / dem.Cols, c0 = cell % dem.Cols;
        double pe = -dir.north, pn = dir.east;
        double step = Math.Min(dem.Dx, dem.Dy);
        int maxK = (int)(maxHalfLength / step);
        (int r, int c) End(int sign)
        {
            int lr = r0, lc = c0;
            for (int k = 1; k <= maxK; k++)
            {
                int c = c0 + (int)Math.Round(sign * k * pe);
                int r = r0 - (int)Math.Round(sign * k * pn);
                if (!dem.InBounds(r, c)) break;
                lr = r; lc = c;
                if (dem[r, c] >= level) break;
            }
            return (lr, lc);
        }
        var a = End(1);
        var b = End(-1);
        return RasterOps.LineCells(dem, a.r, a.c, b.r, b.c);
    }

    private static DamStructure? BuildDownstreamDam(DemGrid dem, DamSpec d, List<int> path, (double east, double north) dir,
        float[] init, bool[] exclude, List<string> log)
    {
        int best = -1;
        double bestDist = double.MaxValue;
        for (int k = 3; k < path.Count; k++)
        {
            double dist = dem.CellCenter(path[k]).DistanceTo(d.Location());
            if (dist < bestDist) { bestDist = dist; best = k; }
        }
        if (best < 0 || bestDist > 2500)
        {
            log.Add($"Cascade: {d.Name} is not on the traced river path ({bestDist / 1000:F1} km away) - skipped.");
            return null;
        }
        int cell = path[best];
        double bed = dem.Z[cell];
        double crest = bed + d.HeightM;
        var original = dem.Z.ToArray();
        var line = CrossValleyLine(dem, cell, dir, crest, 2500);
        var blocked = new bool[dem.Count];
        foreach (int i in line) { blocked[i] = true; dem.Z[i] = (float)Math.Max(dem.Z[i], crest); }

        double level = bed + d.HwOrDefault;
        int seed = path[Math.Max(0, best - 3)];
        var cells = RasterOps.FillBelow(dem, dem.Z, seed, level, blocked, 400_000);
        double vol = 0;
        foreach (int i in cells)
        {
            init[i] = (float)Math.Max(init[i], level - dem.Z[i]);
            vol += (level - dem.Z[i]) * dem.CellArea;
            exclude[i] = true;
        }
        foreach (int i in line) exclude[i] = true;

        var input = new DamInput(d.Type, d.HeightM, d.HwOrDefault, Math.Max(vol, d.ReservoirVolumeM3 * 0.1),
            FailureMode.Overtopping, d.CrestWidthM, d.CrestLengthM);
        var breach = BreachModels.Froehlich2008(input);
        var thalweg = dem.CellCenter(cell);
        var breachCells = line.Where(i => dem.CellCenter(i).DistanceTo(thalweg) <= Math.Max(breach.AverageWidth / 2, dem.Dx)).ToArray();
        var cellSet = new HashSet<int>(cells);
        var probe = line.SelectMany(i => dem.Neighbours4(i / dem.Cols, i % dem.Cols).Select(p => dem.Idx(p.r, p.c)))
            .Where(cellSet.Contains).Distinct().ToArray();

        log.Add($"Cascade: {d.Name} at {bestDist:F0} m from path, crest {crest:F0} m, in-grid reservoir {vol / 1e6:F2} MCM, breach {breach.AverageWidth:F0} m.");
        return new DamStructure
        {
            Name = d.Name,
            Cells = line.ToArray(),
            BreachCells = breachCells,
            OriginalBed = breachCells.Select(i => original[i]).ToArray(),
            Crest = crest,
            Breach = breach,
            ProbeCells = probe
        };
    }
}
