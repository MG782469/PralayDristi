using PralayDrishti.Core.Geo;
using PralayDrishti.Core.Hydraulics;

namespace PralayDrishti.Core.Solvers;

public sealed class NearFieldRequest
{
    public required double[] Chainage { get; init; }     // m, 0 at dam, negative upstream
    public required double[] Bed { get; init; }          // m, absolute
    public required double DamHeight { get; init; }      // water depth at the dam face at failure
    public required double BreachWidth { get; init; }
    public double? StructureDistance { get; init; }      // m downstream (barrage, bridge, powerhouse)
    public double StructureHeight { get; init; } = 15;
    public double StructureWidth { get; init; } = 50;
    public string StructureName { get; init; } = "downstream structure";
    public bool DebrisFlow { get; init; }
    public int Resolution { get; init; } = 30;           // particles over the water depth
    public double Manning { get; init; } = 0.035;
}

public sealed class GaugeComparison
{
    public required double DistanceM { get; init; }
    public required double[] Time { get; init; }
    public required double[] SphDepth { get; init; }
    public required double[] SweDepth { get; init; }
    public required double[] RitterDepth { get; init; }
    public required double[] SphVelocity { get; init; }
    public required double[] SweVelocity { get; init; }
    public double SphPeakDepth => SphDepth.DefaultIfEmpty().Max();
    public double SwePeakDepth => SweDepth.DefaultIfEmpty().Max();
    public double SphArrival { get; init; }
    public double SweArrival { get; init; }
}

public sealed class NearFieldResult
{
    public required double Scale { get; init; }                  // Froude length scale lambda
    public required List<GaugeComparison> Gauges { get; init; }
    public required Hydrograph HandoverHydrograph { get; init; } // prototype m3/s at the handover section
    public required double HandoverDistance { get; init; }
    public required double HandoverTime { get; init; }
    public double? PeakImpactForceKN { get; set; }
    public double? PeakImpactPressureKPa { get; set; }
    public string? StructureName { get; init; }
    public double? StructureDistance { get; init; }
    public List<double> ForceTime { get; init; } = new();
    public List<double> ForceKN { get; init; } = new();
    public List<SphFrame> Frames { get; init; } = new();         // prototype metres
    public double[] BedX { get; init; } = Array.Empty<double>();
    public double[] BedZ { get; init; } = Array.Empty<double>();
    public int Particles { get; init; }
    public double SphWallSeconds { get; init; }
    public double RitterDamUnitDischarge { get; init; }
    public double SphDamUnitDischargePeak { get; init; }
    public List<string> Notes { get; init; } = new();
}

/// <summary>
/// Near-field / far-field coupling:
///   SPH (model scale, Froude similar) handles the collapse and the first few km below the dam.
///   The same set-up is run through the depth-averaged solver in a 1D flume so both are compared in the
///   overlap zone, together with Ritter's exact solution as a sanity anchor.
///   The SPH discharge at the handover section times the breach width becomes the inflow for the 2D far field.
/// </summary>
public static class HybridCoupler
{
    public static NearFieldResult Run(NearFieldRequest req, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        double H = req.DamHeight;
        double lambda = H;                        // model depth = 1
        double sqrtL = Math.Sqrt(lambda);
        double bed0 = Interp(req.Chainage, req.Bed, 0);

        double reservoirLen = 3.0;                 // in dam heights - enough for the negative wave to not matter early
        double domainEnd = 12.0;
        if (req.StructureDistance is double sd)
            domainEnd = Math.Clamp(sd / H + 2.5, 6, 40);

        // model-scale profile, dam base at z = 0
        var px = new List<double>();
        var pz = new List<double>();
        for (int i = 0; i < req.Chainage.Length; i++)
        {
            double xm = req.Chainage[i] / lambda;
            if (xm < -reservoirLen - 1 || xm > domainEnd + 1) continue;
            double zm = (req.Bed[i] - bed0) / lambda;
            if (xm < 0) zm = Math.Min(zm, 0.6);    // DEM over a reservoir is the water surface; keep some water
            px.Add(xm);
            pz.Add(zm);
        }
        if (px.Count < 2 || px[0] > -reservoirLen)
        {
            px.Insert(0, -reservoirLen - 1);
            pz.Insert(0, pz.Count > 0 ? pz[0] : 0);
        }
        if (px[^1] < domainEnd + 1)
        {
            double slope = px.Count > 2 ? (pz[^1] - pz[0]) / (px[^1] - px[0]) : -0.01;
            px.Add(domainEnd + 1);
            pz.Add(pz[^1] + slope * (domainEnd + 1 - px[^2]));
        }
        // strictly increasing x for the interpolator
        var order = px.Select((x, i) => (x, z: pz[i])).GroupBy(p => Math.Round(p.x, 6)).Select(gp => gp.First()).OrderBy(p => p.x).ToList();

        double handoverX = Math.Min(domainEnd - 1.0, 8.0);
        var gaugeX = new List<double> { 0.0, 2.0, 4.0, handoverX };
        if (req.StructureDistance is double s2) gaugeX.Add(Math.Max(0.5, s2 / lambda - 0.3));
        gaugeX = gaugeX.Distinct().OrderBy(x => x).ToList();

        double durationModel = Math.Max(2.5, (handoverX + 4) / (2 * Math.Sqrt(9.81)));
        var sphSetup = new SphSetup
        {
            ProfileX = order.Select(p => p.x).ToArray(),
            ProfileZ = order.Select(p => p.z).ToArray(),
            ReservoirDepth = 1.0,
            ReservoirLength = reservoirLen,
            DomainEnd = domainEnd,
            ParticlesOverDepth = req.Resolution,
            Duration = durationModel,
            GaugeX = gaugeX.ToArray(),
            StructureX = req.StructureDistance is double s3 ? s3 / lambda : null,
            StructureHeight = req.StructureHeight / lambda,
            Density = req.DebrisFlow ? 1800 : 1000,
            Alpha = req.DebrisFlow ? 0.15 : 0.02
        };

        var sph = new SphSolver(sphSetup).Run(new Progress<double>(p => progress?.Report(0.8 * p)), ct);

        // same problem, depth averaged, 1D flume (3 cells wide)
        var flume = RunFlume(order, lambda, gaugeX, durationModel * sqrtL, req.Manning, ct);
        progress?.Report(0.95);

        var comparisons = new List<GaugeComparison>();
        for (int gi = 0; gi < sph.Gauges.Count; gi++)
        {
            var g = sph.Gauges[gi];
            var fg = flume[gi];
            var times = g.Time.Select(t => t * sqrtL).ToArray();
            var sweDepth = times.Select(t => SampleSeries(fg.Time, fg.Depth, t)).ToArray();
            var sweVel = times.Select(t => SampleSeries(fg.Time, fg.Speed, t)).ToArray();
            double xProto = g.X * lambda;
            comparisons.Add(new GaugeComparison
            {
                DistanceM = xProto,
                Time = times,
                SphDepth = g.Depth.Select(d => d * lambda).ToArray(),
                SweDepth = sweDepth,
                RitterDepth = times.Select(t => AnalyticDamBreak.Ritter(H, xProto, t).h).ToArray(),
                SphVelocity = g.Velocity.Select(v => v * sqrtL).ToArray(),
                SweVelocity = sweVel,
                SphArrival = Arrival(times, g.Depth.Select(d => d * lambda).ToArray(), 0.02 * H),
                SweArrival = Arrival(times, sweDepth, 0.02 * H)
            });
        }

        var hand = sph.Gauges.First(g => Math.Abs(g.X - handoverX) < 1e-9);
        var hyd = new Hydrograph { Source = "SPH handover" };
        for (int i = 0; i < hand.Time.Count; i++)
            hyd.Add(hand.Time[i] * sqrtL, Math.Max(0, hand.UnitDischarge[i]) * Math.Pow(lambda, 1.5) * req.BreachWidth);

        var damGauge = sph.Gauges.First(g => g.X == 0.0);
        var notes = new List<string>
        {
            $"SPH run at 1:{lambda:F0} Froude scale, dp = {sph.Dp * lambda:F1} m prototype, {sph.FluidParticles} fluid particles.",
            "Near-field SPH assumes instantaneous collapse (upper bound); the far field uses the routed breach hydrograph.",
            "Differences in the overlap zone come from depth averaging (no vertical accelerations, hydrostatic pressure) vs full 2D-vertical physics at the bore front."
        };
        if (req.DebrisFlow) notes.Add("Debris mode: bulk density 1800 kg/m3 and high dissipation - approximates Chamoli-type debris flow, not a rheology model.");

        var res = new NearFieldResult
        {
            Scale = lambda,
            Gauges = comparisons,
            HandoverHydrograph = hyd,
            HandoverDistance = handoverX * lambda,
            HandoverTime = hyd.Duration,
            StructureName = req.StructureDistance is null ? null : req.StructureName,
            StructureDistance = req.StructureDistance,
            Frames = sph.Frames.Select(f => new SphFrame(f.Time * sqrtL,
                f.X.Select(x => (float)(x * lambda)).ToArray(),
                f.Z.Select(z => (float)(z * lambda + bed0)).ToArray())).ToList(),
            BedX = sph.BedX.Select(x => x * lambda).ToArray(),
            BedZ = sph.BedZ.Select(z => z * lambda + bed0).ToArray(),
            Particles = sph.FluidParticles,
            SphWallSeconds = sph.WallTime.TotalSeconds,
            RitterDamUnitDischarge = AnalyticDamBreak.RitterDamSectionUnitDischarge(H),
            SphDamUnitDischargePeak = damGauge.UnitDischarge.DefaultIfEmpty().Max() * Math.Pow(lambda, 1.5),
            Notes = notes
        };

        if (req.StructureDistance is not null && sph.Force.Count > 0)
        {
            // force per metre width scales with lambda^2 under Froude similarity (same fluid density)
            var fkn = sph.Force.Select(f => Math.Abs(f) * lambda * lambda * req.StructureWidth / 1000).ToList();
            double peak = fkn.Max();
            res.ForceTime.AddRange(sph.ForceTime.Select(t => t * sqrtL));
            res.ForceKN.AddRange(fkn);
            res.PeakImpactForceKN = peak;
            res.PeakImpactPressureKPa = peak / (req.StructureWidth * req.StructureHeight);
        }
        return res;
    }

    private static List<Gauge> RunFlume(List<(double x, double z)> profile, double lambda, List<double> gaugeX,
        double duration, double manning, CancellationToken ct)
    {
        double x0 = profile[0].x * lambda, x1 = profile[^1].x * lambda;
        double dx = Math.Max(2.0, lambda / 25);
        int nx = (int)((x1 - x0) / dx);
        var dem = SyntheticTerrain.Flume(nx, 3, dx);
        double realDx = dem.Dx;
        var init = new float[dem.Count];
        var px = profile.Select(p => p.x * lambda).ToArray();
        var pz = profile.Select(p => p.z * lambda).ToArray();
        for (int c = 0; c < nx; c++)
        {
            double x = x0 + (c + 0.5) * realDx;
            double z = Interp(px, pz, x);
            for (int r = 0; r < 3; r++)
            {
                dem[r, c] = (float)z;
                if (x < 0) init[dem.Idx(r, c)] = (float)Math.Max(0, lambda - z);
            }
        }
        var gauges = gaugeX.Select((g, k) => new Gauge
        {
            Name = $"x{k}",
            Cell = dem.Idx(1, Math.Clamp((int)((g * lambda - x0) / realDx), 0, nx - 1))
        }).ToList();

        var setup = new SweSetup
        {
            Dem = dem,
            Manning = manning,
            InitialDepth = init,
            Gauges = gauges,
            Duration = duration,
            GaugeInterval = duration / 300,
            SnapshotInterval = duration,
            KeepSnapshots = false,
            Cfl = 0.45,
            MaxDt = 1
        };
        new SweSolver(setup).Run(null, ct);
        return gauges;
    }

    private static double SampleSeries(List<float> t, List<float> v, double at)
    {
        if (t.Count == 0) return 0;
        if (at <= t[0]) return v[0];
        for (int i = 1; i < t.Count; i++)
            if (t[i] >= at)
            {
                double f = (at - t[i - 1]) / Math.Max(1e-9, t[i] - t[i - 1]);
                return v[i - 1] + f * (v[i] - v[i - 1]);
            }
        return v[^1];
    }

    private static double Arrival(double[] t, double[] d, double threshold)
    {
        for (int i = 0; i < t.Length; i++)
            if (d[i] > threshold) return t[i];
        return double.NaN;
    }

    internal static double Interp(IReadOnlyList<double> xs, IReadOnlyList<double> ys, double x)
    {
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        for (int i = 1; i < xs.Count; i++)
            if (xs[i] >= x)
            {
                double f = (x - xs[i - 1]) / Math.Max(1e-12, xs[i] - xs[i - 1]);
                return ys[i - 1] + f * (ys[i] - ys[i - 1]);
            }
        return ys[^1];
    }
}
