using PralayDrishti.Application.Common.Models;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Features;
using NetTopologySuite.IO.Converters;
using NetTopologySuite.IO.Esri;
using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Pipeline;

namespace PralayDrishti.Infrastructure.Engine.Export;

/// <summary>Named feature layers built once from an outcome, then written to whatever format is asked for.</summary>
public sealed class LayerBundle
{
    public Dictionary<string, List<IFeature>> Layers { get; } = new();

    public static LayerBundle From(ScenarioOutcome o)
    {
        var b = new LayerBundle();
        var g = o.FarField.Dem;
        var ex = o.Prepared.ExcludeMask;
        b.Layers["flood_depth"] = Vectorizer.Bands(g, o.FarField.MaxDepth, Vectorizer.DepthBands, "depth", ex);
        b.Layers["hazard"] = Vectorizer.Classes(g, o.HazardClass, Hazard.Labels, ex);
        var arrival = o.FarField.ArrivalTime.Select((a, i) => ex[i] ? float.NaN : a).ToArray();
        b.Layers["arrival"] = Vectorizer.Bands(g, arrival, Vectorizer.ArrivalBands, "arrival");
        if (o.Ensemble is { } ens)
        {
            var bands = new[] { new ClassBand(">10%", 0.1, 0.5), new ClassBand(">50%", 0.5, 0.9), new ClassBand(">90%", 0.9, 1.01) };
            b.Layers["probability"] = Vectorizer.Bands(g, ens.Probability, bands, "prob");
        }

        b.Layers["settlements"] = o.Impact.Places.Select(p => (IFeature)new Feature(
            Vectorizer.PointOf(new GeoPoint(p.Lon, p.Lat)), new AttributesTable
            {
                { "name", p.Name }, { "kind", p.Kind }, { "pop", p.Population }, { "depth_m", p.MaxDepth },
                { "vel_ms", p.MaxSpeed }, { "arr_min", p.ArrivalMin ?? -1 }, { "peak_min", p.PeakMin ?? -1 },
                { "hazard", p.HazardLabel }, { "golden", p.GoldenTimeMin ?? -1 }, { "urgency", p.Urgency }
            })).ToList();

        b.Layers["bridges"] = o.Impact.Bridges.Select(x => (IFeature)new Feature(
            Vectorizer.PointOf(new GeoPoint(x.Lon, x.Lat)), new AttributesTable
            {
                { "name", x.Name }, { "overtop", x.Overtopped ? "YES" : "no" }, { "max_ws", x.MaxWaterSurface },
                { "deck", x.DeckLevel }, { "t_min", x.OvertopMin ?? -1 }
            })).ToList();

        b.Layers["road_cuts"] = o.Impact.Roads.SelectMany(r => r.CutPoints.Select(p => (IFeature)new Feature(
            Vectorizer.PointOf(p), new AttributesTable { { "road", r.Name }, { "km_cut", r.FloodedKm }, { "t_min", r.FirstCutMin ?? -1 } }))).ToList();

        b.Layers["staging"] = o.Impact.Staging.Select(s => (IFeature)new Feature(
            Vectorizer.PointOf(new GeoPoint(s.Lon, s.Lat)), new AttributesTable
            {
                { "id", s.Id }, { "type", s.Type }, { "area_ha", s.AreaHa }, { "slope", s.MeanSlope },
                { "road", s.NearRoad ? "yes" : "no" }, { "score", s.Score }
            })).ToList();

        b.Layers["sensors"] = o.Impact.Sensors.Select((s, k) => (IFeature)new Feature(
            Vectorizer.PointOf(new GeoPoint(s.Lon, s.Lat)), new AttributesTable
            {
                { "id", $"S{k + 1}" }, { "chain_km", s.DistanceFromDamKm }, { "arr_min", s.ArrivalMin },
                { "best_lead", s.LeadTimes.Count > 0 ? s.LeadTimes[0].LeadMin : 0 }
            })).ToList();

        var path = o.Prepared.DownstreamPath.Select(i => g.CellCenter(i)).ToList();
        if (path.Count > 1)
            b.Layers["river"] = new List<IFeature> { new Feature(Vectorizer.LineOf(path), new AttributesTable { { "name", "traced river" } }) };
        b.Layers["dam"] = new List<IFeature> { new Feature(Vectorizer.PointOf(o.Prepared.DamPoint), new AttributesTable
        {
            { "name", o.Prepared.Spec.Mode == ScenarioMode.NaturalDam ? "Natural dam" : o.Prepared.Spec.Dam.Name },
            { "breach_w", Math.Round(o.Prepared.Breach.AverageWidth, 1) }, { "tf_min", Math.Round(o.Prepared.Breach.FormationTime / 60, 1) },
            { "qp_m3s", Math.Round(o.Prepared.Hydrograph.Peak) }
        }) };
        return b;
    }
}

public static class GisExporter
{
    public const string WgsPrj =
        "GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]";

    /// <summary>One shapefile per layer, zipped. Polygons/points can't share a .shp.</summary>
    public static byte[] ShapefileZip(LayerBundle bundle, string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), "pd_shp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var (name, feats) in bundle.Layers)
            {
                if (feats.Count == 0) continue;
                string shp = Path.Combine(dir, $"{prefix}_{name}.shp");
                Shapefile.WriteAllFeatures(feats, shp);
                File.WriteAllText(Path.ChangeExtension(shp, ".prj"), WgsPrj);
                File.WriteAllText(Path.ChangeExtension(shp, ".cpg"), "UTF-8");
            }
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var f in Directory.GetFiles(dir))
                    zip.CreateEntryFromFile(f, Path.GetFileName(f));
            return ms.ToArray();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp dir, not worth failing the download over */ }
        }
    }

    public static string GeoJson(LayerBundle bundle, IEnumerable<string>? only = null)
    {
        var fc = new FeatureCollection();
        var names = only?.ToHashSet();
        foreach (var (name, feats) in bundle.Layers)
        {
            if (names != null && !names.Contains(name)) continue;
            foreach (var f in feats)
            {
                var attrs = new AttributesTable { { "layer", name } };
                foreach (var n in f.Attributes.GetNames()) attrs.Add(n, f.Attributes[n]);
                fc.Add(new Feature(f.Geometry, attrs));
            }
        }
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new GeoJsonConverterFactory());
        return JsonSerializer.Serialize(fc, opts);
    }

    public static string Csv(ScenarioOutcome o)
    {
        var sb = new StringBuilder();
        sb.AppendLine("name,kind,lat,lon,population,max_depth_m,max_velocity_ms,arrival_min,peak_min,hazard,golden_time_min,urgency,distance_from_dam_km");
        foreach (var p in o.Impact.Places)
            sb.AppendLine(string.Join(",", Q(p.Name), p.Kind, F(p.Lat, 5), F(p.Lon, 5), p.Population, F(p.MaxDepth), F(p.MaxSpeed),
                F(p.ArrivalMin), F(p.PeakMin), p.HazardLabel, F(p.GoldenTimeMin), Q(p.Urgency), F(p.DistanceFromDamKm)));
        sb.AppendLine();
        sb.AppendLine("bridge,lat,lon,overtopped,max_water_surface_m,deck_level_m,overtop_min");
        foreach (var b in o.Impact.Bridges)
            sb.AppendLine(string.Join(",", Q(b.Name), F(b.Lat, 5), F(b.Lon, 5), b.Overtopped, F(b.MaxWaterSurface), F(b.DeckLevel), F(b.OvertopMin)));
        sb.AppendLine();
        sb.AppendLine("time_s,discharge_m3s,reservoir_level_m");
        var h = o.Prepared.Hydrograph;
        for (int i = 0; i < h.Time.Count; i++)
            sb.AppendLine($"{F(h.Time[i], 0)},{F(h.Discharge[i], 1)},{(double.IsNaN(h.Level[i]) ? "" : F(h.Level[i], 2))}");
        return sb.ToString();
    }

    /// <summary>ESRI ASCII grid - opens in QGIS/ArcGIS without any plugin.</summary>
    public static string AsciiGrid(DemGrid g, float[] values, float noData = -9999)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ncols {g.Cols}");
        sb.AppendLine($"nrows {g.Rows}");
        sb.AppendLine($"xllcorner {F(g.West, 8)}");
        sb.AppendLine($"yllcorner {F(g.South, 8)}");
        sb.AppendLine($"cellsize {F(g.CellSize, 10)}");
        sb.AppendLine($"NODATA_value {noData}");
        for (int r = 0; r < g.Rows; r++)
        {
            for (int c = 0; c < g.Cols; c++)
            {
                float v = values[g.Idx(r, c)];
                sb.Append(float.IsNaN(v) ? noData.ToString(CultureInfo.InvariantCulture) : v.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(c == g.Cols - 1 ? '\n' : ' ');
            }
        }
        return sb.ToString();
    }

    private static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
    private static string F(double v, int d = 2) => v.ToString("F" + d, CultureInfo.InvariantCulture);
    private static string F(double? v, int d = 1) => v is null ? "" : F(v.Value, d);
}
