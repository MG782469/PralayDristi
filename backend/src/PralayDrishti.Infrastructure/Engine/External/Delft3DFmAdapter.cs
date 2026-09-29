using System.Diagnostics;
using System.Globalization;
using System.Text;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Pipeline;
using PralayDrishti.Infrastructure.Engine.Solvers;

namespace PralayDrishti.Infrastructure.Engine.External;

/// <summary>
/// Builds a complete D-Flow FM (Delft3D FM) model from the same set-up the internal solver uses, runs it through a
/// configurable command (native install or the Deltares Docker image) and reads the map file back into a SweResult.
/// Without an installation the model folder is still produced, zipped and offered as a download so a hydrologist
/// can run it on the lab workstation.
/// </summary>
public sealed class Delft3DFmAdapter : IFarFieldRunner
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly string _workRoot;
    private readonly string? _commandTemplate;     // e.g. "docker run --rm -v {dir}:/data -w /data deltares/delft3dfm run_dflowfm.sh {mdu}"
    private readonly TimeSpan _timeout;

    public Delft3DFmAdapter(string workRoot, string? commandTemplate, TimeSpan? timeout = null)
    {
        _workRoot = workRoot;
        _commandTemplate = string.IsNullOrWhiteSpace(commandTemplate) ? null : commandTemplate;
        _timeout = timeout ?? TimeSpan.FromHours(3);
    }

    public string Name => "Delft3D FM (D-Flow FM)";
    public bool IsConfigured => _commandTemplate != null;

    public SweResult Run(SweSetup setup, IProgress<double>? progress, CancellationToken ct)
    {
        if (_commandTemplate == null)
            throw new InvalidOperationException("Delft3D FM is not configured (Solvers:Delft3DCommand). Model files can still be exported.");
        string dir = Path.Combine(_workRoot, "d3d_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6]);
        var model = WriteModel(setup, dir, "dambreak");
        progress?.Report(0.02);

        string cmd = _commandTemplate.Replace("{dir}", dir).Replace("{mdu}", Path.GetFileName(model.MduPath));
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c " + cmd)
            : new ProcessStartInfo("/bin/sh", new[] { "-c", cmd });
        psi.WorkingDirectory = dir;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("could not start Delft3D FM");
        var log = new StringBuilder();
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            log.AppendLine(e.Data);
            // D-Flow FM prints "Sim. time done ... %" lines; good enough for a progress bar
            int pct = e.Data.IndexOf('%');
            if (pct > 3 && double.TryParse(e.Data[(pct - 6)..pct].Trim(), NumberStyles.Float, Inv, out double p))
                progress?.Report(Math.Clamp(p / 100, 0, 1));
        };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) log.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        var sw = Stopwatch.StartNew();
        while (!proc.WaitForExit(1000))
        {
            if (ct.IsCancellationRequested || sw.Elapsed > _timeout)
            {
                try { proc.Kill(true); } catch { /* already gone */ }
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException("Delft3D FM run exceeded the time limit");
            }
        }
        System.IO.File.WriteAllText(Path.Combine(dir, "run.log"), log.ToString());
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"Delft3D FM exited with code {proc.ExitCode}. See {dir}/run.log");

        string map = Directory.GetFiles(dir, "*_map.nc", SearchOption.AllDirectories).FirstOrDefault()
                     ?? throw new FileNotFoundException("D-Flow FM produced no map file");
        var res = ReadMap(map, setup, model.FaceToCell, sw.Elapsed);
        progress?.Report(1);
        return res;
    }

    public sealed record ModelFiles(string Directory, string MduPath, int[] FaceToCell);

    /// <summary>Writes .mdu, UGRID net file, source/sink and boundary forcing. Cells never reached by water are left out.</summary>
    public static ModelFiles WriteModel(SweSetup s, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var g = s.Dem;

        // keep the mesh manageable: cells within the valley (below the highest plausible flood level)
        double top = double.MinValue;
        foreach (var src in s.Inflows) foreach (int i in src.Cells) top = Math.Max(top, g.Z[i]);
        foreach (var st in s.Structures) top = Math.Max(top, st.Crest);
        top += 150;
        var active = new bool[g.Count];
        for (int i = 0; i < g.Count; i++) active[i] = g.Z[i] < top;
        foreach (var src in s.Inflows) foreach (int i in src.Cells) active[i] = true;

        var nodeIndex = new Dictionary<long, int>();
        var nodeX = new List<double>();
        var nodeY = new List<double>();
        var nodeZSum = new List<double>();
        var nodeZCnt = new List<int>();
        var faces = new List<int[]>();
        var faceToCell = new List<int>();

        int Node(int r, int c, float z)
        {
            long key = (long)r * (g.Cols + 1) + c;
            if (!nodeIndex.TryGetValue(key, out int id))
            {
                id = nodeX.Count;
                nodeIndex[key] = id;
                nodeX.Add(g.West + c * g.CellSize);
                nodeY.Add(g.North - r * g.CellSize);
                nodeZSum.Add(0);
                nodeZCnt.Add(0);
            }
            nodeZSum[id] += z;
            nodeZCnt[id]++;
            return id;
        }

        for (int r = 0; r < g.Rows; r++)
            for (int c = 0; c < g.Cols; c++)
            {
                int i = g.Idx(r, c);
                if (!active[i]) continue;
                float z = g.Z[i];
                foreach (var st in s.Structures) if (st.Cells.Contains(i)) z = (float)Math.Max(z, st.Crest);
                // counter-clockwise: SW, SE, NE, NW (1-based in the file)
                faces.Add(new[] { Node(r + 1, c, z) + 1, Node(r + 1, c + 1, z) + 1, Node(r, c + 1, z) + 1, Node(r, c, z) + 1 });
                faceToCell.Add(i);
            }

        var edgeSet = new Dictionary<(int, int), int>();
        var edges = new List<int[]>();
        foreach (var f in faces)
            for (int k = 0; k < 4; k++)
            {
                int a = f[k], b = f[(k + 1) % 4];
                var key = a < b ? (a, b) : (b, a);
                if (edgeSet.ContainsKey(key)) continue;
                edgeSet[key] = edges.Count;
                edges.Add(new[] { key.Item1, key.Item2 });
            }

        var nc = new NetCdf3.File();
        nc.GlobalAttrs.Add(new NetCdf3.Attr { Name = "Conventions", Value = "CF-1.8 UGRID-1.0 Deltares-0.10" });
        nc.GlobalAttrs.Add(new NetCdf3.Attr { Name = "source", Value = "PralayDrishti" });
        nc.AddDim("mesh2d_nNodes", nodeX.Count);
        nc.AddDim("mesh2d_nEdges", edges.Count);
        nc.AddDim("mesh2d_nFaces", faces.Count);
        nc.AddDim("mesh2d_nMax_face_nodes", 4);
        nc.AddDim("Two", 2);

        nc.AddVar("mesh2d", NetCdf3.NcType.Int, Array.Empty<string>(), new[] { 0 },
            ("cf_role", "mesh_topology"), ("long_name", "Topology data of 2D mesh"), ("topology_dimension", 2),
            ("node_coordinates", "mesh2d_node_x mesh2d_node_y"), ("node_dimension", "mesh2d_nNodes"),
            ("edge_node_connectivity", "mesh2d_edge_nodes"), ("edge_dimension", "mesh2d_nEdges"),
            ("face_node_connectivity", "mesh2d_face_nodes"), ("face_dimension", "mesh2d_nFaces"),
            ("max_face_nodes_dimension", "mesh2d_nMax_face_nodes"));
        nc.AddVar("wgs84", NetCdf3.NcType.Int, Array.Empty<string>(), new[] { 0 },
            ("name", "WGS 84"), ("epsg", 4326), ("grid_mapping_name", "latitude_longitude"),
            ("longitude_of_prime_meridian", 0.0), ("semi_major_axis", 6378137.0), ("inverse_flattening", 298.257223563),
            ("EPSG_code", "EPSG:4326"));
        nc.AddVar("mesh2d_node_x", NetCdf3.NcType.Double, new[] { "mesh2d_nNodes" }, nodeX.ToArray(),
            ("standard_name", "longitude"), ("units", "degrees_east"), ("mesh", "mesh2d"), ("location", "node"));
        nc.AddVar("mesh2d_node_y", NetCdf3.NcType.Double, new[] { "mesh2d_nNodes" }, nodeY.ToArray(),
            ("standard_name", "latitude"), ("units", "degrees_north"), ("mesh", "mesh2d"), ("location", "node"));
        nc.AddVar("mesh2d_node_z", NetCdf3.NcType.Double, new[] { "mesh2d_nNodes" },
            nodeZSum.Select((z, k) => z / nodeZCnt[k]).ToArray(),
            ("standard_name", "altitude"), ("units", "m"), ("mesh", "mesh2d"), ("location", "node"), ("positive", "up"));
        nc.AddVar("mesh2d_edge_nodes", NetCdf3.NcType.Int, new[] { "mesh2d_nEdges", "Two" },
            edges.SelectMany(e => e).ToArray(), ("cf_role", "edge_node_connectivity"), ("mesh", "mesh2d"), ("start_index", 1));
        nc.AddVar("mesh2d_face_nodes", NetCdf3.NcType.Int, new[] { "mesh2d_nFaces", "mesh2d_nMax_face_nodes" },
            faces.SelectMany(f => f).ToArray(), ("cf_role", "face_node_connectivity"), ("mesh", "mesh2d"), ("start_index", 1),
            ("_FillValue", -999));
        NetCdf3.Write(nc, Path.Combine(dir, $"{name}_net.nc"));

        // breach inflow as a source (discharge only, no salinity/temperature)
        var ext = new StringBuilder();
        int k2 = 0;
        foreach (var src in s.Inflows)
        {
            k2++;
            var p = g.CellCenter(src.Cells[0]);
            System.IO.File.WriteAllText(Path.Combine(dir, $"breach{k2}.pli"),
                $"breach{k2}\n    1    2\n{F(p.Lon)} {F(p.Lat)}\n");
            var tim = new StringBuilder();
            var h = src.Hydrograph;
            for (int i = 0; i < h.Time.Count; i++) tim.AppendLine($"{F(h.Time[i] / 60)} {F(h.Discharge[i])}");
            tim.AppendLine($"{F(s.Duration / 60 + 1)} 0");
            System.IO.File.WriteAllText(Path.Combine(dir, $"breach{k2}.tim"), tim.ToString());
            ext.AppendLine("QUANTITY=discharge_salinity_temperature_sorsin");
            ext.AppendLine($"FILENAME=breach{k2}.pli");
            ext.AppendLine("FILETYPE=9");
            ext.AppendLine("METHOD=1");
            ext.AppendLine("OPERAND=O");
            ext.AppendLine("AREA=1");
            ext.AppendLine();
        }

        // free outflow: low water level boundary along the whole domain edge
        var ring = new List<GeoPoint>
        {
            new(g.West - g.CellSize * 0.5, g.North + g.CellSize * 0.5), new(g.East + g.CellSize * 0.5, g.North + g.CellSize * 0.5),
            new(g.East + g.CellSize * 0.5, g.South - g.CellSize * 0.5), new(g.West - g.CellSize * 0.5, g.South - g.CellSize * 0.5),
            new(g.West - g.CellSize * 0.5, g.North + g.CellSize * 0.5)
        };
        var pli = new StringBuilder($"outflow\n    {ring.Count}    2\n");
        foreach (var p in ring) pli.AppendLine($"{F(p.Lon)} {F(p.Lat)}");
        System.IO.File.WriteAllText(Path.Combine(dir, "outflow.pli"), pli.ToString());
        double minZ = g.Z.Min() - 5;
        for (int i = 1; i <= ring.Count; i++)
            System.IO.File.WriteAllText(Path.Combine(dir, $"outflow_{i:D4}.tim"), $"0 {F(minZ)}\n{F(s.Duration / 60 + 1)} {F(minZ)}\n");
        ext.AppendLine("QUANTITY=waterlevelbnd");
        ext.AppendLine("FILENAME=outflow.pli");
        ext.AppendLine("FILETYPE=9");
        ext.AppendLine("METHOD=3");
        ext.AppendLine("OPERAND=O");
        System.IO.File.WriteAllText(Path.Combine(dir, $"{name}.ext"), ext.ToString());

        double n = s.ManningField?.Average(x => (double)x) ?? s.Manning;
        string mdu = $"""
# D-Flow FM model generated by PralayDrishti {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
[General]
Program                = D-Flow FM
FileVersion            = 1.09
AutoStart              = 0

[geometry]
NetFile                = {name}_net.nc
BedlevType             = 3
Conveyance2D           = -1
Bedslope               = 0

[numerics]
CFLMax                 = 0.7
AdvecType              = 33
Limtypmom              = 4
Limtypsa               = 4
Epshu                  = 1.0e-3

[physics]
UnifFrictCoef          = {F(n)}
UnifFrictType          = 1
Vicouv                 = 1
Ag                     = 9.81
Rhomean                = 1000

[time]
RefDate                = 20000101
Tunit                  = S
DtUser                 = 60
DtMax                  = 10
DtInit                 = 1
TStart                 = 0
TStop                  = {F(s.Duration)}

[restart]
RestartFile            =

[external forcing]
ExtForceFile           = {name}.ext

[output]
OutputDir              = DFM_OUTPUT_{name}
MapFormat              = 1
NcFormat               = 3
MapInterval            = {F(s.SnapshotInterval)}
HisInterval            = 0
RstInterval            = 0
Wrimap_waterlevel_s1   = 1
Wrimap_waterdepth      = 1
Wrimap_velocity_vector = 1
Wrimap_velocity_magnitude = 1
""";
        string mduPath = Path.Combine(dir, $"{name}.mdu");
        System.IO.File.WriteAllText(mduPath, mdu);
        System.IO.File.WriteAllText(Path.Combine(dir, "README.txt"),
            "Run with:  dflowfm " + name + ".mdu   (or run_dflowfm.sh / run_dflowfm.bat from the Delft3D FM Suite)\n" +
            "Docker:    docker run --rm -v <this folder>:/data -w /data <deltares image> run_dflowfm.sh " + name + ".mdu\n" +
            "Map output is written as netCDF classic (NcFormat = 3) so PralayDrishti can read it back.\n");
        return new ModelFiles(dir, mduPath, faceToCell.ToArray());
    }

    private static SweResult ReadMap(string path, SweSetup s, int[] faceToCell, TimeSpan wall)
    {
        var g = s.Dem;
        int n = g.Count;
        var maxH = new float[n];
        var maxV = new float[n];
        var maxDv = new float[n];
        var maxWs = new float[n];
        var arrival = new float[n];
        var peak = new float[n];
        Array.Fill(arrival, float.NaN);
        Array.Fill(maxWs, float.NaN);
        var snaps = new List<Snapshot>();

        using var nc = new NetCdf3.Reader(path);
        var times = nc.ReadDoubles("time");
        bool hasDepth = nc.Header.Find("mesh2d_waterdepth") != null;
        bool hasVel = nc.Header.Find("mesh2d_ucmag") != null;
        for (int t = 0; t < times.Length; t++)
        {
            var s1 = nc.ReadDoubles("mesh2d_s1", t);
            var depth = hasDepth ? nc.ReadDoubles("mesh2d_waterdepth", t) : null;
            var vel = hasVel ? nc.ReadDoubles("mesh2d_ucmag", t) : null;
            var snap = new float[n];
            for (int f = 0; f < faceToCell.Length && f < s1.Length; f++)
            {
                int i = faceToCell[f];
                double h = depth?[f] ?? Math.Max(0, s1[f] - g.Z[i]);
                double v = vel?[f] ?? 0;
                snap[i] = (float)h;
                if (h > maxH[i]) { maxH[i] = (float)h; peak[i] = (float)times[t]; }
                if (h > 0.01 && (float.IsNaN(maxWs[i]) || s1[f] > maxWs[i])) maxWs[i] = (float)s1[f];
                if (h > 0.05)
                {
                    maxV[i] = Math.Max(maxV[i], (float)v);
                    maxDv[i] = Math.Max(maxDv[i], (float)(h * v));
                }
                if (float.IsNaN(arrival[i]) && h >= s.ArrivalDepth) arrival[i] = (float)times[t];
            }
            snaps.Add(new Snapshot(times[t], snap));
        }
        foreach (var gauge in s.Gauges)
            foreach (var sn in snaps)
            {
                gauge.Time.Add((float)sn.Time);
                gauge.Depth.Add(sn.Depth[gauge.Cell]);
                gauge.Speed.Add(0);
                gauge.Surface.Add(sn.Depth[gauge.Cell] + g.Z[gauge.Cell]);
            }

        return new SweResult
        {
            Dem = g,
            MaxDepth = maxH,
            MaxSpeed = maxV,
            MaxDv = maxDv,
            ArrivalTime = arrival,
            PeakTime = peak,
            MaxSurface = maxWs,
            Snapshots = snaps,
            Gauges = s.Gauges,
            SimulatedTime = times.Length > 0 ? times[^1] : 0,
            WallTime = wall,
            InflowVolume = s.Inflows.Sum(i => i.Hydrograph.Volume())
        };
    }

    private static string F(double v) => v.ToString("0.########", Inv);
}
