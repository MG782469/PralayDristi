using System.IO.Compression;
using System.Xml.Linq;
using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Export;
using PralayDrishti.Infrastructure.Engine.External;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Pipeline;
using Xunit.Abstractions;

namespace PralayDrishti.EngineTests;

public class ExportTests
{
    private readonly ITestOutputHelper _out;
    public ExportTests(ITestOutputHelper o) => _out = o;

    private static readonly Lazy<ScenarioOutcome> Outcome = new(() =>
    {
        var v = SyntheticTerrain.Build(new GeoPoint(88.6, 27.6), cols: 220, rows: 140);
        var exp = new ExposureSet();
        exp.Points.Add(new ExposurePoint("Gaon A", AssetKind.Village, v.RiverLine[80], 800));
        exp.Points.Add(new ExposurePoint("Gaon B", AssetKind.Village, v.RiverLine[150], 1200));
        exp.Points.Add(new ExposurePoint("Pul", AssetKind.Bridge, v.RiverLine[100], DeckClearance: 5));
        exp.Roads.Add(new ExposureLine("SH-1", "secondary", v.RiverLine.Where((_, i) => i % 8 == 0).ToList()));
        var spec = new ScenarioSpec
        {
            Name = "export test",
            Dam = new DamSpec { Name = "Test", Lon = v.DamSite.Lon, Lat = v.DamSite.Lat, HeightM = 30, ReservoirVolumeM3 = 8e6 },
            DurationHours = 0.75, RunSph = false, EnsembleMembers = 2
        };
        return new ScenarioEngine().Run(spec, v.Dem, exp);
    });

    [Fact]
    public void Archive_roundtrip_keeps_results()
    {
        var o = Outcome.Value;
        string dir = Path.Combine(Path.GetTempPath(), "pd_arch_" + Guid.NewGuid().ToString("N"));
        RunArchive.Save(o, dir);
        var back = RunArchive.Load(dir);
        Assert.Equal(o.FarField.MaxDepth.Length, back.FarField.MaxDepth.Length);
        Assert.Equal(o.FarField.MaxDepth.Max(), back.FarField.MaxDepth.Max());
        Assert.Equal(o.Impact.Places.Count, back.Impact.Places.Count);
        Assert.Equal(o.Prepared.Hydrograph.Peak, back.Prepared.Hydrograph.Peak, 6);
        Assert.Equal(o.FarField.Snapshots.Count, back.FarField.Snapshots.Count);
        Assert.NotNull(back.Ensemble);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void All_exports_produce_valid_files()
    {
        var o = Outcome.Value;
        var bundle = LayerBundle.From(o);
        Assert.NotEmpty(bundle.Layers["flood_depth"]);

        var zip = GisExporter.ShapefileZip(bundle, "test");
        using (var za = new ZipArchive(new MemoryStream(zip)))
        {
            var names = za.Entries.Select(e => e.Name).ToList();
            _out.WriteLine(string.Join(", ", names));
            Assert.Contains(names, n => n.EndsWith("flood_depth.shp"));
            Assert.Contains(names, n => n.EndsWith("flood_depth.dbf"));
            Assert.Contains(names, n => n.EndsWith("flood_depth.prj"));
        }

        var gj = GisExporter.GeoJson(bundle);
        Assert.Contains("FeatureCollection", gj);

        var kml = KmlExporter.Kml(o, bundle, DateTime.UtcNow);
        XDocument.Parse(kml);
        var kmz = KmlExporter.Kmz(o, bundle, DateTime.UtcNow);
        using (var za = new ZipArchive(new MemoryStream(kmz)))
        {
            Assert.NotNull(za.GetEntry("doc.kml"));
            Assert.True(za.Entries.Count(e => e.Name.StartsWith("flood_")) >= 3);
            using var sr = new StreamReader(za.GetEntry("doc.kml")!.Open());
            var doc = XDocument.Parse(sr.ReadToEnd());
            Assert.Contains(doc.Descendants(), e => e.Name.LocalName == "TimeSpan");
        }

        var cap = CapAlertBuilder.Build(o, "T1");
        var capDoc = XDocument.Parse(cap);
        Assert.Equal("urn:oasis:names:tc:emergency:cap:1.2", capDoc.Root!.Name.NamespaceName);
        Assert.Equal(2, capDoc.Descendants().Count(e => e.Name.LocalName == "info"));

        var pdf = SitrepPdf.Build(o, "T1", DateTime.UtcNow);
        Assert.True(pdf.Length > 10_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        var csv = GisExporter.Csv(o);
        Assert.Contains("golden_time_min", csv);
        var asc = GisExporter.AsciiGrid(o.FarField.Dem, o.FarField.MaxDepth);
        Assert.StartsWith("ncols", asc);
        _out.WriteLine($"shp {zip.Length / 1024} KB, kmz {kmz.Length / 1024} KB, pdf {pdf.Length / 1024} KB");
    }

    [Fact]
    public void Delft3D_model_is_written_and_net_file_reads_back()
    {
        var o = Outcome.Value;
        var engine = new ScenarioEngine();
        var setup = engine.BuildSetup(o.Prepared, 1.0, keepSnapshots: true);
        string dir = Path.Combine(Path.GetTempPath(), "pd_d3d_" + Guid.NewGuid().ToString("N"));
        var model = Delft3DFmAdapter.WriteModel(setup, dir, "dambreak");
        Assert.True(File.Exists(model.MduPath));
        Assert.True(File.Exists(Path.Combine(dir, "dambreak.ext")));
        int faces;
        double[] x, fn;
        using (var nc = new NetCdf3.Reader(Path.Combine(dir, "dambreak_net.nc")))
        {
            faces = nc.DimLength("mesh2d_nFaces");
            x = nc.ReadDoubles("mesh2d_node_x");
            fn = nc.ReadDoubles("mesh2d_face_nodes");
        }
        Assert.Equal(model.FaceToCell.Length, faces);
        Assert.Equal(faces * 4, fn.Length);
        Assert.True(x.Min() >= o.Prepared.Dem.West - 1e-6);
        _out.WriteLine($"faces {faces}, nodes {x.Length}");
        Directory.Delete(dir, true);
    }

    [Fact]
    public void DualSphysics_case_is_written()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pd_dsph_" + Guid.NewGuid().ToString("N"));
        var xml = DualSphysicsExporter.Write(new DualSphysicsExporter.Case(
            new[] { -100.0, 0, 500 }, new[] { 2.0, 0, -5 }, 90, 30, 1.0, 10, 30, new[] { 0.0, 100, 200 }, 250, 10), dir);
        XDocument.Parse(File.ReadAllText(xml));
        Assert.True(new FileInfo(Path.Combine(dir, "bed.stl")).Length > 84);
        Directory.Delete(dir, true);
    }
}
