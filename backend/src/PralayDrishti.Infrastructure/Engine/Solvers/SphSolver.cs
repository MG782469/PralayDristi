using System.Diagnostics;

namespace PralayDrishti.Infrastructure.Engine.Solvers;

public sealed class SphSetup
{
    /// <summary>Bed profile in model units, x downstream positive, dam face at x = 0.</summary>
    public required double[] ProfileX { get; init; }
    public required double[] ProfileZ { get; init; }
    public double ReservoirDepth { get; init; } = 1.0;
    public double ReservoirLength { get; init; } = 3.0;
    public double DomainEnd { get; init; } = 12.0;
    public int ParticlesOverDepth { get; init; } = 30;
    public double Duration { get; init; } = 3.0;
    public double[] GaugeX { get; init; } = Array.Empty<double>();
    public double? StructureX { get; init; }
    public double StructureHeight { get; init; } = 0.3;
    public double Density { get; init; } = 1000;
    public double Alpha { get; init; } = 0.02;           // artificial viscosity
    public double SoundSpeedFactor { get; init; } = 15;
    public double FrameInterval { get; init; } = 0.05;
    public int MaxFrameParticles { get; init; } = 2500;
}

public sealed class SphGauge
{
    public required double X { get; init; }
    public List<double> Time { get; } = new();
    public List<double> Depth { get; } = new();
    public List<double> Velocity { get; } = new();
    public List<double> UnitDischarge { get; } = new();
}

public sealed record SphFrame(double Time, float[] X, float[] Z);

public sealed class SphResult
{
    public List<SphGauge> Gauges { get; init; } = new();
    public List<SphFrame> Frames { get; init; } = new();
    public List<double> ForceTime { get; init; } = new();
    public List<double> Force { get; init; } = new();              // N per metre width (model units)
    public List<double> FrontTime { get; init; } = new();
    public List<double> FrontX { get; init; } = new();
    public int FluidParticles { get; init; }
    public int BoundaryParticles { get; init; }
    public long Steps { get; init; }
    public double Dp { get; init; }
    public TimeSpan WallTime { get; init; }
    public double[] BedX { get; init; } = Array.Empty<double>();
    public double[] BedZ { get; init; } = Array.Empty<double>();
}

/// <summary>
/// Weakly compressible SPH (Monaghan 1994) in a 2D vertical section - the same formulation DualSPHysics uses:
/// cubic spline kernel, Tait equation of state, Monaghan artificial viscosity, delta-SPH density diffusion
/// and dynamic boundary particles. Runs at model scale; the coupler handles Froude scaling to the prototype.
/// Good at what depth-averaged models can't do: the violent near-field collapse, the bore, and pressure/impact
/// on a structure right below the dam.
/// </summary>
public sealed class SphSolver
{
    private const double G = 9.81;

    private readonly SphSetup _s;
    private readonly double _dp, _h, _c0, _rho0, _mass, _b;
    private double[] _x = Array.Empty<double>(), _z = Array.Empty<double>();
    private double[] _u = Array.Empty<double>(), _w = Array.Empty<double>();
    private double[] _rho = Array.Empty<double>(), _p = Array.Empty<double>();
    private double[] _ax = Array.Empty<double>(), _az = Array.Empty<double>(), _drho = Array.Empty<double>();
    private double[] _uPrev = Array.Empty<double>(), _wPrev = Array.Empty<double>(), _rhoPrev = Array.Empty<double>();
    private double[] _visc = Array.Empty<double>();
    private int _verletCount;
    private bool[] _fluid = Array.Empty<bool>(), _active = Array.Empty<bool>(), _structure = Array.Empty<bool>();
    private int _n;

    // neighbour grid
    private double _gx0, _gz0, _cell;
    private int _gnx, _gnz;
    private int[] _cellStart = Array.Empty<int>(), _cellCount = Array.Empty<int>(), _sorted = Array.Empty<int>(), _pcell = Array.Empty<int>();

    public SphSolver(SphSetup setup)
    {
        _s = setup;
        _dp = setup.ReservoirDepth / setup.ParticlesOverDepth;
        _h = 1.3 * _dp;
        _rho0 = setup.Density;
        _mass = _rho0 * _dp * _dp;
        _c0 = setup.SoundSpeedFactor * Math.Sqrt(G * setup.ReservoirDepth);
        _b = _c0 * _c0 * _rho0 / 7.0;
        BuildParticles();
    }

    private double Bed(double x)
    {
        var px = _s.ProfileX;
        var pz = _s.ProfileZ;
        if (x <= px[0]) return pz[0];
        if (x >= px[^1]) return pz[^1];
        int i = Array.BinarySearch(px, x);
        if (i >= 0) return pz[i];
        i = ~i;
        double t = (x - px[i - 1]) / (px[i] - px[i - 1]);
        return pz[i - 1] + t * (pz[i] - pz[i - 1]);
    }

    private void BuildParticles()
    {
        var xs = new List<double>();
        var zs = new List<double>();
        var fl = new List<bool>();
        var st = new List<bool>();
        double x0 = -_s.ReservoirLength;
        double level = Bed(0) + _s.ReservoirDepth;

        // fluid block behind the (instantly removed) gate
        for (double x = x0 + _dp; x < -_dp * 0.5; x += _dp)
            for (double z = Bed(x) + _dp; z < level; z += _dp)
            {
                xs.Add(x); zs.Add(z); fl.Add(true); st.Add(false);
            }

        // bed: three layers following the profile
        for (double x = x0 - 2 * _dp; x <= _s.DomainEnd + 3 * _dp; x += _dp)
            for (int k = 0; k < 3; k++)
            {
                xs.Add(x); zs.Add(Bed(x) - k * _dp); fl.Add(false); st.Add(false);
            }

        // upstream wall
        for (double z = Bed(x0) + _dp; z < level + 3 * _dp; z += _dp)
            for (int k = 0; k < 3; k++)
            {
                xs.Add(x0 - k * _dp); zs.Add(z); fl.Add(false); st.Add(false);
            }

        // optional structure (barrage / bridge pier face) - we integrate the force on it
        if (_s.StructureX is double sx)
        {
            double zb = Bed(sx);
            for (double z = zb + _dp; z <= zb + _s.StructureHeight; z += _dp)
                for (int k = 0; k < 2; k++)
                {
                    xs.Add(sx + k * _dp); zs.Add(z); fl.Add(false); st.Add(true);
                }
        }

        _n = xs.Count;
        _x = xs.ToArray();
        _z = zs.ToArray();
        _fluid = fl.ToArray();
        _structure = st.ToArray();
        _active = Enumerable.Repeat(true, _n).ToArray();
        _u = new double[_n];
        _w = new double[_n];
        _rho = new double[_n];
        _p = new double[_n];
        _ax = new double[_n];
        _az = new double[_n];
        _drho = new double[_n];
        _uPrev = new double[_n];
        _wPrev = new double[_n];
        _visc = new double[_n];

        // hydrostatic initial density so the column doesn't "bounce" at t=0
        for (int i = 0; i < _n; i++)
        {
            double depth = _fluid[i] ? Math.Max(0, level - _z[i]) : 0;
            _rho[i] = _rho0 * Math.Pow(1 + _rho0 * G * depth / _b, 1.0 / 7.0);
        }
        _rhoPrev = (double[])_rho.Clone();

        _cell = 2 * _h;
        _gx0 = x0 - 5 * _dp;
        double gx1 = _s.DomainEnd + 5 * _dp;
        _gz0 = _z.Min() - 5 * _dp;
        double gz1 = level + 5 * _s.ReservoirDepth;
        _gnx = (int)Math.Ceiling((gx1 - _gx0) / _cell) + 1;
        _gnz = (int)Math.Ceiling((gz1 - _gz0) / _cell) + 1;
        _cellStart = new int[_gnx * _gnz];
        _cellCount = new int[_gnx * _gnz];
        _sorted = new int[_n];
        _pcell = new int[_n];
    }

    public SphResult Run(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var gauges = _s.GaugeX.Select(x => new SphGauge { X = x }).ToList();
        var result = new SphResult
        {
            Gauges = gauges,
            FluidParticles = _fluid.Count(f => f),
            BoundaryParticles = _fluid.Count(f => !f),
            Dp = _dp,
            BedX = _s.ProfileX,
            BedZ = _s.ProfileZ
        };

        double t = 0, nextFrame = 0, nextGauge = 0;
        double gaugeEvery = _s.Duration / 300;
        long steps = 0;
        while (t < _s.Duration)
        {
            ct.ThrowIfCancellationRequested();
            BuildGrid();
            double structureForce = ComputeForces();

            if (t >= nextGauge)
            {
                SampleGauges(gauges, t);
                result.ForceTime.Add(t);
                result.Force.Add(structureForce);
                result.FrontTime.Add(t);
                result.FrontX.Add(FrontPosition());
                nextGauge += gaugeEvery;
                progress?.Report(t / _s.Duration);
            }
            if (t >= nextFrame)
            {
                result.Frames.Add(Frame(t));
                nextFrame += _s.FrameInterval;
            }

            double dt = TimeStep();
            Integrate(dt);
            t += dt;
            steps++;
        }
        result.Frames.Add(Frame(t));

        return new SphResult
        {
            Gauges = result.Gauges,
            Frames = result.Frames,
            ForceTime = result.ForceTime,
            Force = result.Force,
            FrontTime = result.FrontTime,
            FrontX = result.FrontX,
            FluidParticles = result.FluidParticles,
            BoundaryParticles = result.BoundaryParticles,
            Dp = _dp,
            Steps = steps,
            WallTime = sw.Elapsed,
            BedX = result.BedX,
            BedZ = result.BedZ
        };
    }

    private void BuildGrid()
    {
        Array.Clear(_cellCount);
        for (int i = 0; i < _n; i++)
        {
            if (!_active[i]) { _pcell[i] = -1; continue; }
            int cx = (int)Math.Floor((_x[i] - _gx0) / _cell);
            int cz = (int)Math.Floor((_z[i] - _gz0) / _cell);
            if (cx < 0 || cz < 0 || cx >= _gnx || cz >= _gnz)
            {
                _active[i] = false;     // left the domain
                _pcell[i] = -1;
                continue;
            }
            int c = cz * _gnx + cx;
            _pcell[i] = c;
            _cellCount[c]++;
        }
        int acc = 0;
        for (int c = 0; c < _cellCount.Length; c++)
        {
            _cellStart[c] = acc;
            acc += _cellCount[c];
        }
        var fill = new int[_cellCount.Length];
        for (int i = 0; i < _n; i++)
        {
            int c = _pcell[i];
            if (c < 0) continue;
            _sorted[_cellStart[c] + fill[c]++] = i;
        }
    }

    private double ComputeForces()
    {
        for (int i = 0; i < _n; i++)
        {
            double p = _b * (Math.Pow(_rho[i] / _rho0, 7) - 1);
            // no tension: free-surface particles would otherwise clump (tensile instability)
            _p[i] = p < 0 ? 0 : p;
        }

        double sigma = 10.0 / (7.0 * Math.PI * _h * _h);
        double h2 = _h * _h, eta2 = 0.01 * h2, supp2 = 4 * h2;
        const double delta = 0.1;
        double totalForce = 0;
        object gate = new();

        Parallel.For(0, _n, () => 0.0, (i, _, localForce) =>
        {
            _ax[i] = 0; _az[i] = 0; _drho[i] = 0; _visc[i] = 0;
            if (!_active[i]) return localForce;
            double viscMax = 0;
            double axi = 0, azi = _fluid[i] ? -G : 0, dri = 0;
            double xi = _x[i], zi = _z[i], ui = _u[i], wi = _w[i], ri = _rho[i], pi = _p[i];
            int cx = (int)((xi - _gx0) / _cell), cz = (int)((zi - _gz0) / _cell);
            double fStruct = 0;

            for (int oz = -1; oz <= 1; oz++)
            {
                int nz = cz + oz;
                if (nz < 0 || nz >= _gnz) continue;
                for (int ox = -1; ox <= 1; ox++)
                {
                    int nx = cx + ox;
                    if (nx < 0 || nx >= _gnx) continue;
                    int c = nz * _gnx + nx;
                    int end = _cellStart[c] + _cellCount[c];
                    for (int k = _cellStart[c]; k < end; k++)
                    {
                        int j = _sorted[k];
                        if (j == i) continue;
                        if (!_fluid[i] && !_fluid[j]) continue;   // boundary-boundary pairs do nothing
                        double dx = xi - _x[j], dz = zi - _z[j];
                        double r2 = dx * dx + dz * dz;
                        if (r2 >= supp2 || r2 < 1e-18) continue;
                        double r = Math.Sqrt(r2);
                        double q = r / _h;
                        double dwdr = q < 1
                            ? sigma / _h * (-3 * q + 2.25 * q * q)
                            : -0.75 * sigma / _h * (2 - q) * (2 - q);
                        double gx = dwdr * dx / r, gz = dwdr * dz / r;
                        double du = ui - _u[j], dw = wi - _w[j];
                        double rj = _rho[j];

                        // continuity
                        dri += _mass * (du * gx + dw * gz);
                        if (_fluid[i] && _fluid[j])
                        {
                            // delta-SPH diffusion term (Molteni & Colagrossi 2009)
                            double psi = 2 * (rj - ri) / (r2 + eta2);
                            dri += delta * _h * _c0 * psi * (-dx * gx - dz * gz) * _mass / rj;
                        }

                        double vr = du * dx + dw * dz;
                        double pij = 0;
                        double mu = _h * vr / (r2 + eta2);
                        if (Math.Abs(mu) > viscMax) viscMax = Math.Abs(mu);
                        if (vr < 0)
                        {
                            double cbar = 0.5 * (Sound(ri) + Sound(rj));
                            pij = -_s.Alpha * cbar * mu / (0.5 * (ri + rj));
                        }
                        double coef = -_mass * (pi / (ri * ri) + _p[j] / (rj * rj) + pij);
                        if (_fluid[i])
                        {
                            axi += coef * gx;
                            azi += coef * gz;
                        }
                        else if (_structure[i])
                        {
                            // force on this wall particle from the fluid neighbour, x component, per unit width
                            fStruct += coef * gx * _mass;
                        }
                    }
                }
            }
            _ax[i] = axi;
            _az[i] = azi;
            _drho[i] = dri;
            _visc[i] = viscMax;
            return localForce + fStruct;
        }, f => { lock (gate) totalForce += f; });

        return totalForce;
    }

    private double Sound(double rho) => _c0 * Math.Pow(rho / _rho0, 3);

    private double TimeStep()
    {
        double amax = 0, cmax = _c0, vmax = 0;
        for (int i = 0; i < _n; i++)
        {
            if (!_active[i]) continue;
            vmax = Math.Max(vmax, _visc[i]);
            cmax = Math.Max(cmax, Sound(_rho[i]));
            if (_fluid[i]) amax = Math.Max(amax, Math.Sqrt(_ax[i] * _ax[i] + _az[i] * _az[i]));
        }
        double dtF = amax > 0 ? Math.Sqrt(_h / amax) : 1;
        double dtCv = _h / (cmax + vmax);
        return 0.2 * Math.Min(dtF, dtCv);
    }

    /// <summary>
    /// Verlet scheme as in DualSPHysics: leapfrog over two steps, with a plain Euler step every 40 steps so the
    /// odd/even solutions don't decouple.
    /// </summary>
    private void Integrate(double dt)
    {
        bool euler = ++_verletCount % 40 == 0;
        double dt2 = 0.5 * dt * dt;
        Parallel.For(0, _n, i =>
        {
            if (!_active[i]) return;
            double rhoNew = euler ? _rho[i] + dt * _drho[i] : _rhoPrev[i] + 2 * dt * _drho[i];
            _rhoPrev[i] = _rho[i];
            if (!_fluid[i])
            {
                // dynamic boundaries: density evolves, but never below rest (avoids sticking)
                _rho[i] = Math.Max(rhoNew, _rho0);
                return;
            }
            _rho[i] = Math.Max(rhoNew, 0.9 * _rho0);
            double uNew = euler ? _u[i] + dt * _ax[i] : _uPrev[i] + 2 * dt * _ax[i];
            double wNew = euler ? _w[i] + dt * _az[i] : _wPrev[i] + 2 * dt * _az[i];
            _x[i] += dt * _u[i] + dt2 * _ax[i];
            _z[i] += dt * _w[i] + dt2 * _az[i];
            _uPrev[i] = _u[i];
            _wPrev[i] = _w[i];
            _u[i] = uNew;
            _w[i] = wNew;
            if (_x[i] > _s.DomainEnd) _active[i] = false;
        });
    }

    private double FrontPosition()
    {
        double front = double.MinValue;
        for (int i = 0; i < _n; i++)
            if (_active[i] && _fluid[i] && _x[i] > front && _z[i] - Bed(_x[i]) < 4 * _dp + _s.ReservoirDepth)
                front = _x[i];
        return front;
    }

    private void SampleGauges(List<SphGauge> gauges, double t)
    {
        foreach (var g in gauges)
        {
            double top = double.MinValue, usum = 0;
            int cnt = 0;
            for (int i = 0; i < _n; i++)
            {
                if (!_active[i] || !_fluid[i] || Math.Abs(_x[i] - g.X) > _dp) continue;
                top = Math.Max(top, _z[i]);
                usum += _u[i];
                cnt++;
            }
            double depth = cnt == 0 ? 0 : Math.Max(0, top + 0.5 * _dp - Bed(g.X));
            double umean = cnt == 0 ? 0 : usum / cnt;
            g.Time.Add(t);
            g.Depth.Add(depth);
            g.Velocity.Add(umean);
            g.UnitDischarge.Add(cnt == 0 ? 0 : usum * _dp * _dp / (2 * _dp));
        }
    }

    private SphFrame Frame(double t)
    {
        var idx = Enumerable.Range(0, _n).Where(i => _active[i] && _fluid[i]).ToList();
        int stride = Math.Max(1, idx.Count / _s.MaxFrameParticles);
        var xs = new List<float>();
        var zs = new List<float>();
        for (int k = 0; k < idx.Count; k += stride)
        {
            xs.Add((float)_x[idx[k]]);
            zs.Add((float)_z[idx[k]]);
        }
        return new SphFrame(t, xs.ToArray(), zs.ToArray());
    }
}
