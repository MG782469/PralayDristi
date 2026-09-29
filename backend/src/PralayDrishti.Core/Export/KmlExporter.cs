using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using PralayDrishti.Core.Analysis;
using PralayDrishti.Core.Pipeline;

namespace PralayDrishti.Core.Export;

/// <summary>
/// KML for Google Earth. The KMZ variant carries a time-animated flood (one GroundOverlay per snapshot with a
/// TimeSpan), so the time slider in Google Earth plays the flood moving down the valley.
/// </summary>
public static class KmlExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly string[] DepthColors = { "c8ffe9c6", "d7f5c482", "e1de9242", "ebbf6621", "f0963c18", "f56e1428" };  // aabbggrr
    private static readonly string[] HazardKml = { "00000000", "c8e6d8ad", "d7a0edff", "e14fc4fe", "eb3c8dfd", "f01c1ae3", "f5260080" };

    public static string Kml(ScenarioOutcome o, LayerBundle bundle, DateTime eventStartUtc) =>
        Build(o, bundle, eventStartUtc, null);

    public static byte[] Kmz(ScenarioOutcome o, LayerBundle bundle, DateTime eventStartUtc)
    {
        var overlays = new List<(string file, double t0, double t1, byte[] png)>();
        var snaps = o.FarField.Snapshots;
        var g = o.FarField.Dem;
        for (int k = 0; k < snaps.Count; k++)
        {
            double t0 = snaps[k].Time;
            double t1 = k + 1 < snaps.Count ? snaps[k + 1].Time : t0 + 600;
            var png = RasterRenderer.RenderGrid(g, snaps[k].Depth, LayerKind.Snapshot, o.Prepared.ExcludeMask);
            overlays.Add(($"files/flood_{k:D3}.png", t0, t1, png));
        }
        var maxPng = RasterRenderer.RenderGrid(g, o.FarField.MaxDepth, LayerKind.Depth, o.Prepared.ExcludeMask);
        string kml = Build(o, bundle, eventStartUtc, overlays.Select(x => (x.file, x.t0, x.t1)).ToList());

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var doc = zip.CreateEntry("doc.kml");
            using (var w = new StreamWriter(doc.Open(), new UTF8Encoding(false))) w.Write(kml);
            foreach (var (file, _, _, png) in overlays)
            {
                var e = zip.CreateEntry(file, CompressionLevel.Fastest);
                using var s = e.Open();
                s.Write(png);
            }
            var me = zip.CreateEntry("files/max_depth.png", CompressionLevel.Fastest);
            using (var s = me.Open()) s.Write(maxPng);
        }
        return ms.ToArray();
    }

    private static string Build(ScenarioOutcome o, LayerBundle bundle, DateTime start,
        List<(string file, double t0, double t1)>? overlays)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using (var w = XmlWriter.Create(sb, settings))
        {
            w.WriteStartDocument();
            w.WriteStartElement("kml", "http://www.opengis.net/kml/2.2");
            w.WriteStartElement("Document");
            w.WriteElementString("name", $"PralayDrishti - {o.Prepared.Spec.Name}");
            w.WriteElementString("description",
                $"Dam-break inundation. Breach {o.Prepared.Breach.Method}, Qp {o.Prepared.Hydrograph.Peak:F0} m3/s. " +
                $"Flooded {o.Impact.FloodedAreaKm2:F2} km2, population at risk {o.Impact.PopulationAtRisk}.");

            for (int k = 0; k < DepthColors.Length; k++) PolyStyle(w, $"depth{k}", DepthColors[k]);
            for (int k = 1; k < HazardKml.Length; k++) PolyStyle(w, $"haz{k}", HazardKml[k]);
            IconStyle(w, "village", "ff0000ff", "http://maps.google.com/mapfiles/kml/shapes/homegardenbusiness.png");
            IconStyle(w, "bridge", "ff00a5ff", "http://maps.google.com/mapfiles/kml/shapes/triangle.png");
            IconStyle(w, "staging", "ff00ff00", "http://maps.google.com/mapfiles/kml/shapes/heliport.png");
            IconStyle(w, "sensor", "ffffff00", "http://maps.google.com/mapfiles/kml/shapes/target.png");
            IconStyle(w, "dam", "ff0000aa", "http://maps.google.com/mapfiles/kml/shapes/caution.png");

            if (overlays != null)
            {
                Folder(w, "Flood progression (use time slider)", () =>
                {
                    var g = o.FarField.Dem;
                    foreach (var (file, t0, t1) in overlays)
                    {
                        w.WriteStartElement("GroundOverlay");
                        w.WriteElementString("name", $"T+{t0 / 60:F0} min");
                        w.WriteStartElement("TimeSpan");
                        w.WriteElementString("begin", Iso(start.AddSeconds(t0)));
                        w.WriteElementString("end", Iso(start.AddSeconds(t1)));
                        w.WriteEndElement();
                        w.WriteStartElement("Icon");
                        w.WriteElementString("href", file);
                        w.WriteEndElement();
                        LatLonBox(w, g.North, g.South, g.East, g.West);
                        w.WriteEndElement();
                    }
                });
            }

            Folder(w, "Maximum flood depth", () =>
            {
                int k = 0;
                foreach (var f in bundle.Layers["flood_depth"])
                {
                    int idx = Array.FindIndex(Vectorizer.DepthBands, b => b.Label == (string)f.Attributes["depth"]);
                    Placemark(w, (string)f.Attributes["depth"], $"#depth{Math.Max(0, idx)}", f.Geometry,
                        $"Area {f.Attributes["area_km2"]} km2");
                    k++;
                }
            });
            Folder(w, "Hazard (depth x velocity)", () =>
            {
                foreach (var f in bundle.Layers["hazard"])
                {
                    string cls = (string)f.Attributes["class"];
                    Placemark(w, (string)f.Attributes["label"], $"#haz{cls[1..]}", f.Geometry, $"Area {f.Attributes["area_km2"]} km2");
                }
            }, visible: false);

            Folder(w, "Settlements at risk", () =>
            {
                foreach (var p in o.Impact.Places)
                {
                    string desc = $"<table><tr><td>Population</td><td>{p.Population}</td></tr>" +
                                  $"<tr><td>Arrival</td><td>{p.ArrivalMin:F0} min</td></tr>" +
                                  $"<tr><td>Peak depth</td><td>{p.MaxDepth:F1} m</td></tr>" +
                                  $"<tr><td>Velocity</td><td>{p.MaxSpeed:F1} m/s</td></tr>" +
                                  $"<tr><td>Hazard</td><td>{p.HazardLabel}</td></tr>" +
                                  $"<tr><td>Golden time</td><td>{p.GoldenTimeMin:F0} min</td></tr>" +
                                  $"<tr><td colspan='2'>{p.Advice}</td></tr></table>";
                    Placemark(w, $"{p.Name} ({p.ArrivalMin:F0} min)", "#village", Vectorizer.PointOf(new(p.Lon, p.Lat)), desc,
                        p.ArrivalMin is double a ? start.AddMinutes(a) : null);
                }
            });
            Folder(w, "Bridges", () =>
            {
                foreach (var b in o.Impact.Bridges)
                    Placemark(w, $"{b.Name}{(b.Overtopped ? " - OVERTOPPED" : "")}", "#bridge", Vectorizer.PointOf(new(b.Lon, b.Lat)),
                        $"Max water surface {b.MaxWaterSurface} m, deck {b.DeckLevel} m, overtop at {b.OvertopMin} min");
            });
            Folder(w, "Relief staging / helicopter landing zones", () =>
            {
                foreach (var s in o.Impact.Staging)
                    Placemark(w, $"{s.Type} {s.AreaHa} ha", "#staging", Vectorizer.PointOf(new(s.Lon, s.Lat)),
                        $"Slope {s.MeanSlope} deg, {s.DistanceToFloodKm} km from flood edge, road access: {(s.NearRoad ? "yes" : "no")}");
            });
            Folder(w, "Suggested water-level sensors", () =>
            {
                int k = 1;
                foreach (var s in o.Impact.Sensors)
                    Placemark(w, $"Sensor S{k++} ({s.DistanceFromDamKm} km)", "#sensor", Vectorizer.PointOf(new(s.Lon, s.Lat)),
                        string.Join("<br/>", s.LeadTimes.Select(l => $"{l.Place}: {l.LeadMin:F0} min lead")));
            });
            Folder(w, "Dam and river", () =>
            {
                foreach (var f in bundle.Layers["dam"])
                    Placemark(w, (string)f.Attributes["name"], "#dam", f.Geometry,
                        $"Breach {f.Attributes["breach_w"]} m, tf {f.Attributes["tf_min"]} min, Qp {f.Attributes["qp_m3s"]} m3/s");
                if (bundle.Layers.TryGetValue("river", out var river))
                    foreach (var f in river) Placemark(w, "River (traced)", null, f.Geometry, "");
            });

            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        return sb.ToString();
    }

    private static void Folder(XmlWriter w, string name, Action body, bool visible = true)
    {
        w.WriteStartElement("Folder");
        w.WriteElementString("name", name);
        if (!visible) w.WriteElementString("visibility", "0");
        body();
        w.WriteEndElement();
    }

    private static void PolyStyle(XmlWriter w, string id, string color)
    {
        w.WriteStartElement("Style");
        w.WriteAttributeString("id", id);
        w.WriteStartElement("LineStyle");
        w.WriteElementString("color", "66ffffff");
        w.WriteElementString("width", "0.5");
        w.WriteEndElement();
        w.WriteStartElement("PolyStyle");
        w.WriteElementString("color", color);
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void IconStyle(XmlWriter w, string id, string color, string href)
    {
        w.WriteStartElement("Style");
        w.WriteAttributeString("id", id);
        w.WriteStartElement("IconStyle");
        w.WriteElementString("color", color);
        w.WriteElementString("scale", "1.1");
        w.WriteStartElement("Icon");
        w.WriteElementString("href", href);
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void Placemark(XmlWriter w, string name, string? style, Geometry geom, string description, DateTime? when = null)
    {
        w.WriteStartElement("Placemark");
        w.WriteElementString("name", name);
        w.WriteStartElement("description");
        w.WriteCData(description);
        w.WriteEndElement();
        if (when is DateTime t)
        {
            // appear when the flood reaches it and stay
            w.WriteStartElement("TimeSpan");
            w.WriteElementString("begin", Iso(t));
            w.WriteEndElement();
        }
        if (style != null) w.WriteElementString("styleUrl", style);
        WriteGeometry(w, geom);
        w.WriteEndElement();
    }

    private static void WriteGeometry(XmlWriter w, Geometry g)
    {
        switch (g)
        {
            case Point p:
                w.WriteStartElement("Point");
                w.WriteElementString("coordinates", $"{p.X.ToString(Inv)},{p.Y.ToString(Inv)},0");
                w.WriteEndElement();
                break;
            case LineString l:
                w.WriteStartElement("LineString");
                w.WriteElementString("tessellate", "1");
                w.WriteElementString("coordinates", Coords(l.Coordinates));
                w.WriteEndElement();
                break;
            case Polygon poly:
                w.WriteStartElement("Polygon");
                w.WriteStartElement("outerBoundaryIs");
                Ring(w, poly.ExteriorRing);
                w.WriteEndElement();
                foreach (var hole in poly.InteriorRings)
                {
                    w.WriteStartElement("innerBoundaryIs");
                    Ring(w, hole);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                break;
            case GeometryCollection gc:
                w.WriteStartElement("MultiGeometry");
                foreach (var part in gc.Geometries) WriteGeometry(w, part);
                w.WriteEndElement();
                break;
        }
    }

    private static void Ring(XmlWriter w, LineString ring)
    {
        w.WriteStartElement("LinearRing");
        w.WriteElementString("coordinates", Coords(ring.Coordinates));
        w.WriteEndElement();
    }

    private static string Coords(Coordinate[] cs) =>
        string.Join(" ", cs.Select(c => $"{c.X.ToString("F6", Inv)},{c.Y.ToString("F6", Inv)},0"));

    private static void LatLonBox(XmlWriter w, double n, double s, double e, double west)
    {
        w.WriteStartElement("LatLonBox");
        w.WriteElementString("north", n.ToString(Inv));
        w.WriteElementString("south", s.ToString(Inv));
        w.WriteElementString("east", e.ToString(Inv));
        w.WriteElementString("west", west.ToString(Inv));
        w.WriteEndElement();
    }

    private static string Iso(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", Inv);
}
