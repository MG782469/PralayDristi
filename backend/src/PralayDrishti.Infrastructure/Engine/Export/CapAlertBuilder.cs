using PralayDrishti.Application.Common.Models;
using System.Globalization;
using System.Text;
using System.Xml;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.Infrastructure.Engine.Export;

/// <summary>
/// Common Alerting Protocol 1.2 (OASIS). NDMA's SACHET platform consumes CAP, so an alert produced here can be
/// pushed into the national dissemination chain as-is. English + Hindi info blocks.
/// </summary>
public static class CapAlertBuilder
{
    private const string Ns = "urn:oasis:names:tc:emergency:cap:1.2";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed record Options(
        string Sender = "pralaydrishti@hadr.local",
        string SenderName = "PralayDrishti Dam-Break Decision Support",
        string Status = "Exercise",        // Actual | Exercise | Test - default to Exercise, a human must flip it
        DateTime? Sent = null,
        bool Probabilistic = false);

    public static string Build(ScenarioOutcome o, string runId, Options? opt = null)
    {
        opt ??= new Options();
        var sent = opt.Sent ?? DateTime.UtcNow;
        var ist = TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(sent, DateTimeKind.Utc), ist);
        string sentStr = local.ToString("yyyy-MM-ddTHH:mm:ss", Inv) + "+05:30";

        var imp = o.Impact;
        var first = imp.Places.FirstOrDefault(p => p.ArrivalMin != null);
        string damName = o.Prepared.Spec.Mode == ScenarioMode.NaturalDam ? "landslide lake" : o.Prepared.Spec.Dam.Name;
        string severity = imp.Places.Any(p => p.HazardClass >= 5) ? "Extreme" : imp.Places.Any(p => p.HazardClass >= 3) ? "Severe" : "Moderate";
        string urgency = first?.ArrivalMin is < 120 ? "Immediate" : "Expected";
        string certainty = opt.Probabilistic ? "Possible" : "Likely";

        var extent = Vectorizer.Extent(o.FarField.Dem, o.FarField.MaxDepth, 0.3, o.Prepared.ExcludeMask);
        var polygons = CapPolygons(extent);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using (var w = XmlWriter.Create(sb, settings))
        {
            w.WriteStartDocument();
            w.WriteStartElement("alert", Ns);
            w.WriteElementString("identifier", Ns, $"PD-{runId}");
            w.WriteElementString("sender", Ns, opt.Sender);
            w.WriteElementString("sent", Ns, sentStr);
            w.WriteElementString("status", Ns, opt.Status);
            w.WriteElementString("msgType", Ns, "Alert");
            w.WriteElementString("scope", Ns, "Public");

            string headlineEn = $"Flood warning: failure of {damName}. Flood reaches {first?.Name ?? "downstream villages"} in about {first?.ArrivalMin:F0} minutes.";
            string descEn = $"Modelled failure of {damName}. Peak outflow about {o.Prepared.Hydrograph.Peak:F0} m3/s. " +
                            $"{imp.SettlementsAffected} settlements and about {imp.PopulationAtRisk} people in the flood zone. " +
                            $"Flooded area {imp.FloodedAreaKm2:F1} km2, maximum depth {imp.MaxDepth:F1} m. " +
                            string.Join(" ", imp.Places.Take(8).Select(p => $"{p.Name}: arrival {p.ArrivalMin:F0} min, depth {p.MaxDepth:F1} m."));
            string instrEn = "Move immediately to high ground away from the river. Do not cross bridges or low roads. Help children, elderly and disabled first. Do not return until authorities declare it safe.";
            Info(w, "en-IN", headlineEn, descEn, instrEn, severity, urgency, certainty, opt, o, polygons, sent);

            string headlineHi = $"बाढ़ चेतावनी: {damName} टूटने की आशंका। लगभग {first?.ArrivalMin:F0} मिनट में पानी पहुँच सकता है।";
            string descHi = $"लगभग {imp.PopulationAtRisk} लोग और {imp.SettlementsAffected} गाँव बाढ़ क्षेत्र में हैं। अधिकतम गहराई {imp.MaxDepth:F1} मीटर।";
            string instrHi = "तुरंत नदी से दूर ऊँचे स्थान पर जाएँ। पुल या निचली सड़क पार न करें। बच्चों, बुज़ुर्गों और दिव्यांगों की पहले मदद करें।";
            Info(w, "hi-IN", headlineHi, descHi, instrHi, severity, urgency, certainty, opt, o, polygons, sent);

            w.WriteEndElement();
            w.WriteEndDocument();
        }
        return sb.ToString();
    }

    private static void Info(XmlWriter w, string lang, string headline, string desc, string instr, string severity,
        string urgency, string certainty, Options opt, ScenarioOutcome o, List<string> polygons, DateTime sent)
    {
        w.WriteStartElement("info", Ns);
        w.WriteElementString("language", Ns, lang);
        w.WriteElementString("category", Ns, "Safety");
        w.WriteElementString("category", Ns, "Infra");
        w.WriteElementString("event", Ns, "Dam Break Flood");
        w.WriteElementString("responseType", Ns, "Evacuate");
        w.WriteElementString("urgency", Ns, urgency);
        w.WriteElementString("severity", Ns, severity);
        w.WriteElementString("certainty", Ns, certainty);
        w.WriteElementString("expires", Ns, sent.AddHours(24).ToString("yyyy-MM-ddTHH:mm:ss", Inv) + "+00:00");
        w.WriteElementString("senderName", Ns, opt.SenderName);
        w.WriteElementString("headline", Ns, headline);
        w.WriteElementString("description", Ns, desc);
        w.WriteElementString("instruction", Ns, instr);
        Param(w, "PeakDischargeM3s", o.Prepared.Hydrograph.Peak.ToString("F0", Inv));
        Param(w, "PopulationAtRisk", o.Impact.PopulationAtRisk.ToString(Inv));
        Param(w, "FloodedAreaKm2", o.Impact.FloodedAreaKm2.ToString("F2", Inv));

        w.WriteStartElement("area", Ns);
        w.WriteElementString("areaDesc", Ns, "Modelled flood zone downstream of " +
            (o.Prepared.Spec.Mode == ScenarioMode.NaturalDam ? "landslide lake" : o.Prepared.Spec.Dam.Name));
        foreach (var p in polygons) w.WriteElementString("polygon", Ns, p);
        foreach (var pl in o.Impact.Places.Where(p => p.HazardClass >= 3).Take(40))
            w.WriteElementString("circle", Ns, $"{pl.Lat.ToString("F5", Inv)},{pl.Lon.ToString("F5", Inv)} 1.0");
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void Param(XmlWriter w, string name, string value)
    {
        w.WriteStartElement("parameter", Ns);
        w.WriteElementString("valueName", Ns, name);
        w.WriteElementString("value", Ns, value);
        w.WriteEndElement();
    }

    /// <summary>CAP polygons are "lat,lon" pairs, closed, and receivers choke on huge ones - keep them small.</summary>
    private static List<string> CapPolygons(Geometry? extent)
    {
        var list = new List<string>();
        if (extent == null) return list;
        var polys = new List<Polygon>();
        for (int i = 0; i < extent.NumGeometries; i++)
            if (extent.GetGeometryN(i) is Polygon p) polys.Add(p);
        foreach (var p in polys.OrderByDescending(p => p.Area).Take(10))
        {
            Geometry s = p;
            double tol = 0.0005;
            while (s.Coordinates.Length > 100 && tol < 0.05)
            {
                s = DouglasPeuckerSimplifier.Simplify(p, tol);
                tol *= 2;
            }
            var shell = s is Polygon sp ? sp.ExteriorRing.Coordinates : s.Coordinates;
            if (shell.Length < 4) continue;
            list.Add(string.Join(" ", shell.Select(c => $"{c.Y.ToString("F5", Inv)},{c.X.ToString("F5", Inv)}")));
        }
        return list;
    }
}
