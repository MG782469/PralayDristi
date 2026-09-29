using System.Diagnostics;

namespace PralayDrishti.Infrastructure.Engine.Solvers;

/// <summary>
/// 2D depth-averaged shallow water solver, first order finite volume on the DEM grid.
///  - HLL Riemann solver, upwinded transverse momentum
///  - hydrostatic reconstruction (Audusse et al. 2004) -> well balanced, positive depths, wet/dry fronts
///  - semi-implicit Manning friction
///  - adaptive dt (CFL) and an "active window" so we don't sweep dry mountains for hours
/// This is the same equation set D-Flow FM solves (depth averaged, hydrostatic); it is our always-available
/// far-field engine. Delft3D itself is driven through Delft3DFmAdapter when it's installed.
/// </summary>
public sealed class SweSolver
{
    private const double G = 9.81;
    private const double Dry = 1e-3;
    private const double MaxVel = 30;

    private readonly SweSetup _s;
    private readonly int _nx, _ny;
    private readonly double _dx, _dy;
    private readonly double[] _h, _hu, _hv, _z;
    private readonly double[] _n2;

    // face fluxes. x-face (r,c) is the west face of cell (r,c); y-face (r,c) is the north face.
    private readonly double[] _xh, _xmL, _xmR, _xt;
    private readonly double[] _yh, _ymL, _ymR, _yt;

    public SweSolver(SweSetup setup)
    {
        _s = setup;
        var g = setup.Dem;
        _nx = g.Cols;
        _ny = g.Rows;
        _dx = g.Dx;
        _dy = g.Dy;
        int n = g.Count;
        _h = new double[n];
        _hu = new double[n];
        _hv = new double[n];
        _z = new double[n];
        _n2 = new double[n];
        for (int i = 0; i < n; i++)
        {
            _z[i] = g.Z[i];
            double m = setup.ManningField?[i] ?? setup.Manning;
            _n2[i] = m * m;
            if (setup.InitialDepth != null) _h[i] = Math.Max(0, setup.InitialDepth[i]);
        }
        foreach (var st in setup.Structures)
            foreach (int i in st.Cells)
                _z[i] = Math.Max(_z[i], st.Crest);

        _xh = new double[_ny * (_nx + 1)];
        _xmL = new double[_xh.Length];
        _xmR = new double[_xh.Length];
        _xt = new double[_xh.Length];
        _yh = new double[(_ny + 1) * _nx];
        _ymL = new double[_yh.Length];
        _ymR = new double[_yh.Length];
        _yt = new double[_yh.Length];
    }

    public SweResult Run(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int n = _nx * _ny;
        var maxH = new float[n];
        var maxV = new float[n];
        var maxDv = new float[n];
        var maxWs = new float[n];
        var arrival = new float[n];
        var peakT = new float[n];
        Array.Fill(arrival, float.NaN);
        for (int i = 0; i < n; i++) maxWs[i] = float.NaN;

        var snaps = new List<Snapshot>();
        var failures = new List<(string, double)>();
        double cellArea = _dx * _dy;
        double initialVol = _h.Sum() * cellArea;
        double inflowVol = 0, outflowVol = 0;

        double t = 0, nextSnap = 0, nextGauge = 0, nextProgress = 0;
        long steps = 0;
        var (r0, r1, c0, c1) = ActiveWindow();

        while (t < _s.Duration)
        {
            ct.ThrowIfCancellationRequested();
            if (t >= nextSnap && _s.KeepSnapshots)
            {
                snaps.Add(new Snapshot(t, _h.Select(v => (float)v).ToArray()));
                nextSnap += _s.SnapshotInterval;
            }
            if (t >= nextGauge)
            {
                RecordGauges(t);
                nextGauge += _s.GaugeInterval;
            }

            double dt = ComputeDt(r0, r1, c0, c1);
            dt = Math.Min(dt, _s.Duration - t);
            if (_s.KeepSnapshots) dt = Math.Min(dt, Math.Max(1e-3, nextSnap - t));

            UpdateStructures(t, dt, failures);
            ComputeFluxes(r0, r1, c0, c1);
            outflowVol += BoundaryOutflow(r0, r1, c0, c1) * dt;
            Update(r0, r1, c0, c1, dt);
            inflowVol += ApplyInflows(t, dt);

            t += dt;
            steps++;
            TrackMaxima(t, r0, r1, c0, c1, maxH, maxV, maxDv, maxWs, arrival, peakT);

            if (steps % 20 == 0) (r0, r1, c0, c1) = ActiveWindow();
            if (t >= nextProgress)
            {
                progress?.Report(t / _s.Duration);
                nextProgress += _s.Duration / 200;
            }
        }
        if (_s.KeepSnapshots) snaps.Add(new Snapshot(t, _h.Select(v => (float)v).ToArray()));
        RecordGauges(t);

        var bed = _s.Dem.WithValues(_z.Select(v => (float)v).ToArray());
        return new SweResult
        {
            Dem = bed,
            MaxDepth = maxH,
            MaxSpeed = maxV,
            MaxDv = maxDv,
            ArrivalTime = arrival,
            PeakTime = peakT,
            MaxSurface = maxWs,
            Snapshots = snaps,
            Gauges = _s.Gauges,
            Failures = failures,
            InflowVolume = inflowVol,
            InitialVolume = initialVol,
            FinalVolume = _h.Sum() * cellArea,
            OutflowVolume = outflowVol,
            Steps = steps,
            SimulatedTime = t,
            WallTime = sw.Elapsed
        };
    }

    /// <summary>Snapshot of the current depth field, used by the flume comparison.</summary>
    public double DepthAt(int idx) => _h[idx];
    public double VelocityXAt(int idx) => _h[idx] > Dry ? _hu[idx] / _h[idx] : 0;

    private (int r0, int r1, int c0, int c1) ActiveWindow()
    {
        int r0 = int.MaxValue, r1 = -1, c0 = int.MaxValue, c1 = -1;
        void Mark(int i)
        {
            int r = i / _nx, c = i % _nx;
            if (r < r0) r0 = r;
            if (r > r1) r1 = r;
            if (c < c0) c0 = c;
            if (c > c1) c1 = c;
        }
        for (int i = 0; i < _h.Length; i++)
            if (_h[i] > Dry) Mark(i);
        foreach (var src in _s.Inflows)
            foreach (int i in src.Cells) Mark(i);
        foreach (var st in _s.Structures)
            foreach (int i in st.Cells) Mark(i);
        if (r1 < 0) return (0, 0, 0, 0);
        const int margin = 3;
        return (Math.Max(0, r0 - margin), Math.Min(_ny - 1, r1 + margin),
                Math.Max(0, c0 - margin), Math.Min(_nx - 1, c1 + margin));
    }

    private double ComputeDt(int r0, int r1, int c0, int c1)
    {
        double maxSx = 0, maxSy = 0;
        object gate = new();
        Parallel.For(r0, r1 + 1, () => (0.0, 0.0), (r, _, acc) =>
        {
            double sx = acc.Item1, sy = acc.Item2;
            for (int c = c0; c <= c1; c++)
            {
                int i = r * _nx + c;
                double h = _h[i];
                if (h <= Dry) continue;
                double cel = Math.Sqrt(G * h);
                sx = Math.Max(sx, Math.Abs(_hu[i] / h) + cel);
                sy = Math.Max(sy, Math.Abs(_hv[i] / h) + cel);
            }
            return (sx, sy);
        }, acc =>
        {
            lock (gate)
            {
                maxSx = Math.Max(maxSx, acc.Item1);
                maxSy = Math.Max(maxSy, acc.Item2);
            }
        });
        if (maxSx < 1e-9 && maxSy < 1e-9) return _s.MaxDt;
        double dt = _s.Cfl / (maxSx / _dx + maxSy / _dy);
        return Math.Min(dt, _s.MaxDt);
    }

    private void ComputeFluxes(int r0, int r1, int c0, int c1)
    {
        // x faces: c from c0 .. c1+1 (west face of c, the last one is the east face of c1)
        Parallel.For(r0, r1 + 1, r =>
        {
            for (int c = c0; c <= c1 + 1; c++)
            {
                int li = r * _nx + Math.Max(c - 1, 0);
                int ri = r * _nx + Math.Min(c, _nx - 1);
                if (c == 0) li = ri;                // transmissive ghost
                if (c == _nx) ri = li;
                int f = r * (_nx + 1) + c;
                FaceFlux(_h[li], _hu[li], _hv[li], _z[li], _h[ri], _hu[ri], _hv[ri], _z[ri],
                    out _xh[f], out _xmL[f], out _xmR[f], out _xt[f]);
            }
        });
        // y faces: normal direction is "down the rows" (southward); swap u/v roles
        Parallel.For(r0, r1 + 2, r =>
        {
            for (int c = c0; c <= c1; c++)
            {
                int ti = Math.Max(r - 1, 0) * _nx + c;
                int bi = Math.Min(r, _ny - 1) * _nx + c;
                if (r == 0) ti = bi;
                if (r == _ny) bi = ti;
                int f = r * _nx + c;
                FaceFlux(_h[ti], _hv[ti], _hu[ti], _z[ti], _h[bi], _hv[bi], _hu[bi], _z[bi],
                    out _yh[f], out _ymL[f], out _ymR[f], out _yt[f]);
            }
        });
    }

    private static void FaceFlux(double hL, double qnL, double qtL, double zL,
                                 double hR, double qnR, double qtR, double zR,
                                 out double fh, out double fmL, out double fmR, out double ft)
    {
        double zI = Math.Max(zL, zR);
        double hLs = Math.Max(0, hL + zL - zI);
        double hRs = Math.Max(0, hR + zR - zI);
        double uL = hL > Dry ? Math.Clamp(qnL / hL, -MaxVel, MaxVel) : 0;
        double vL = hL > Dry ? qtL / hL : 0;
        double uR = hR > Dry ? Math.Clamp(qnR / hR, -MaxVel, MaxVel) : 0;
        double vR = hR > Dry ? qtR / hR : 0;

        double fm;
        if (hLs <= Dry && hRs <= Dry)
        {
            fh = 0; fm = 0; ft = 0;
        }
        else
        {
            double cL = Math.Sqrt(G * hLs), cR = Math.Sqrt(G * hRs);
            double sL, sR;
            if (hLs <= Dry) { sL = uR - 2 * cR; sR = uR + cR; }
            else if (hRs <= Dry) { sL = uL - cL; sR = uL + 2 * cL; }
            else
            {
                sL = Math.Min(uL - cL, uR - cR);
                sR = Math.Max(uL + cL, uR + cR);
            }

            double fhL = hLs * uL, fmL0 = hLs * uL * uL + 0.5 * G * hLs * hLs;
            double fhR = hRs * uR, fmR0 = hRs * uR * uR + 0.5 * G * hRs * hRs;
            if (sL >= 0) { fh = fhL; fm = fmL0; }
            else if (sR <= 0) { fh = fhR; fm = fmR0; }
            else
            {
                double inv = 1.0 / (sR - sL);
                fh = (sR * fhL - sL * fhR + sL * sR * (hRs - hLs)) * inv;
                fm = (sR * fmL0 - sL * fmR0 + sL * sR * (hRs * uR - hLs * uL)) * inv;
            }
            ft = fh > 0 ? fh * vL : fh * vR;
        }
        // hydrostatic reconstruction correction terms
        fmL = fm + 0.5 * G * (hL * hL - hLs * hLs);
        fmR = fm + 0.5 * G * (hR * hR - hRs * hRs);
    }

    private void Update(int r0, int r1, int c0, int c1, double dt)
    {
        double ax = dt / _dx, ay = dt / _dy;
        Parallel.For(r0, r1 + 1, r =>
        {
            for (int c = c0; c <= c1; c++)
            {
                int i = r * _nx + c;
                int fw = r * (_nx + 1) + c, fe = fw + 1;
                int fn = r * _nx + c, fs = (r + 1) * _nx + c;

                double h = _h[i] - ax * (_xh[fe] - _xh[fw]) - ay * (_yh[fs] - _yh[fn]);
                double hu = _hu[i] - ax * (_xmL[fe] - _xmR[fw]) - ay * (_yt[fs] - _yt[fn]);
                double hv = _hv[i] - ax * (_xt[fe] - _xt[fw]) - ay * (_ymL[fs] - _ymR[fn]);

                if (h <= Dry)
                {
                    _h[i] = Math.Max(h, 0);
                    _hu[i] = 0;
                    _hv[i] = 0;
                    continue;
                }
                // semi-implicit Manning: tau = g n^2 |u| u / h^(1/3)
                double u = hu / h, v = hv / h;
                double speed = Math.Sqrt(u * u + v * v);
                double f = 1 + dt * G * _n2[i] * speed / Math.Pow(h, 4.0 / 3.0);
                _h[i] = h;
                _hu[i] = hu / f;
                _hv[i] = hv / f;
            }
        });
    }

    private double BoundaryOutflow(int r0, int r1, int c0, int c1)
    {
        double q = 0;
        if (c0 == 0) for (int r = r0; r <= r1; r++) q -= _xh[r * (_nx + 1)] * _dy;
        if (c1 == _nx - 1) for (int r = r0; r <= r1; r++) q += _xh[r * (_nx + 1) + _nx] * _dy;
        if (r0 == 0) for (int c = c0; c <= c1; c++) q -= _yh[c] * _dx;
        if (r1 == _ny - 1) for (int c = c0; c <= c1; c++) q += _yh[_ny * _nx + c] * _dx;
        return q;
    }

    private double ApplyInflows(double t, double dt)
    {
        double vol = 0;
        double cellArea = _dx * _dy;
        foreach (var src in _s.Inflows)
        {
            double q = src.Hydrograph.At(t + 0.5 * dt);
            if (q <= 0) continue;
            int n = src.Cells.Length;
            double dh = q * dt / (n * cellArea);
            double width = n * Math.Min(_dx, _dy);
            foreach (int i in src.Cells)
            {
                double depth = Math.Max(_h[i], 1.0);
                double uin = Math.Min(q / (width * depth), 12);
                _h[i] += dh;
                // injected water carries momentum along the valley, otherwise it piles up at the breach
                _hu[i] += dh * uin * src.DirEast;
                _hv[i] += dh * uin * -src.DirNorth;
            }
            vol += q * dt;
        }
        return vol;
    }

    private void UpdateStructures(double t, double dt, List<(string, double)> failures)
    {
        foreach (var st in _s.Structures)
        {
            if (st.FailedAt is null)
            {
                bool fail = st.ForcedFailureTime is double ft && t >= ft;
                if (!fail && st.ProbeCells.Length > 0)
                {
                    double ws = st.ProbeCells.Max(i => _h[i] > Dry ? _h[i] + _z[i] : double.MinValue);
                    fail = ws > st.Crest + st.OvertopTrigger;
                }
                if (!fail) continue;
                st.FailedAt = t;
                failures.Add((st.Name, t));
            }

            double frac = Math.Clamp((t + dt - st.FailedAt!.Value) / Math.Max(st.Breach.FormationTime, 1), 0, 1);
            frac = 0.5 * (1 - Math.Cos(Math.PI * frac));
            for (int k = 0; k < st.BreachCells.Length; k++)
            {
                int i = st.BreachCells[k];
                double target = st.Crest - (st.Crest - st.OriginalBed[k]) * frac;
                if (target < _z[i]) _z[i] = target;
            }
        }
    }

    private void RecordGauges(double t)
    {
        foreach (var g in _s.Gauges)
        {
            int i = g.Cell;
            double h = _h[i];
            double sp = h > Dry ? Math.Sqrt(_hu[i] * _hu[i] + _hv[i] * _hv[i]) / h : 0;
            g.Time.Add((float)t);
            g.Depth.Add((float)h);
            g.Speed.Add((float)Math.Min(sp, MaxVel));
            g.Surface.Add((float)(h + _z[i]));
        }
    }

    private void TrackMaxima(double t, int r0, int r1, int c0, int c1,
        float[] maxH, float[] maxV, float[] maxDv, float[] maxWs, float[] arrival, float[] peakT)
    {
        float arrivalDepth = (float)_s.ArrivalDepth;
        Parallel.For(r0, r1 + 1, r =>
        {
            for (int c = c0; c <= c1; c++)
            {
                int i = r * _nx + c;
                double h = _h[i];
                if (h <= Dry) continue;
                float hf = (float)h;
                if (hf > maxH[i]) { maxH[i] = hf; peakT[i] = (float)t; }
                float ws = (float)(h + _z[i]);
                if (float.IsNaN(maxWs[i]) || ws > maxWs[i]) maxWs[i] = ws;
                if (h > 0.05)
                {
                    double sp = Math.Min(Math.Sqrt(_hu[i] * _hu[i] + _hv[i] * _hv[i]) / h, MaxVel);
                    if (sp > maxV[i]) maxV[i] = (float)sp;
                    float dv = (float)(h * sp);
                    if (dv > maxDv[i]) maxDv[i] = dv;
                }
                if (float.IsNaN(arrival[i]) && h >= arrivalDepth) arrival[i] = (float)t;
            }
        });
    }
}
