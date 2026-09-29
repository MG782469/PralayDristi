using Microsoft.Extensions.Logging;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Geo;

namespace PralayDrishti.Infrastructure.Services;

/// <summary>
/// Settlements, bridges, hospitals, schools and roads for the impact analysis.
/// Local files in data/exposure (curated / uploaded GeoJSON) + OpenStreetMap via Overpass when online.
/// Overpass answers are cached per bounding box so the second run of a valley is offline-capable.
/// </summary>
public sealed class ExposureProvider : IExposureProvider
{
    private static readonly Dictionary<string, int> PopulationByClass = new()
    {
        ["hamlet"] = 150, ["isolated_dwelling"] = 20, ["village"] = 800, ["town"] = 8000, ["city"] = 100000, ["suburb"] = 5000
    };

    private readonly DataPaths _paths;
    private readonly AppOptions _opt;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<ExposureProvider> _log;

    public ExposureProvider(DataPaths paths, AppOptions opt, IHttpClientFactory http, ILogger<ExposureProvider> log)
    {
        _paths = paths;
        _opt = opt;
        _http = http;
        _log = log;
    }

    public async Task<(ExposureSet set, List<string> sources)> ForAreaAsync(BBox box, CancellationToken ct)
    {
        var sources = new List<string>();
        var set = LoadLocal(box, sources);
        if (_opt.UseOsm)
        {
            try
            {
                var osm = await FetchOsmAsync(box, ct);
                if (osm.Points.Count + osm.Roads.Count > 0)
                {
                    set = set.Merge(osm);
                    sources.Add($"OpenStreetMap: {osm.Points.Count} points, {osm.Roads.Count} roads");
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning("OSM exposure unavailable: {Msg}", ex.Message);
                sources.Add("OpenStreetMap unavailable (offline?) - using local exposure only");
            }
        }
        return (set, sources);
    }

    public async Task<string> GetGeoJsonAsync(BoundingBox b, bool includeOsm, CancellationToken ct)
    {
        if (b.East - b.West > 2 || b.North - b.South > 2) throw new BusinessRuleException("Box too large (max 2 x 2 degrees).");
        var box = new BBox(b.West, b.South, b.East, b.North);
        var set = includeOsm ? (await ForAreaAsync(box, ct)).set : LoadLocal(box);
        var features = new JsonArray();
        foreach (var p in set.Points)
            features.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["geometry"] = new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(p.Location.Lon, p.Location.Lat) },
                ["properties"] = new JsonObject { ["name"] = p.Name, ["kind"] = p.Kind.ToString(), ["population"] = p.Population, ["source"] = p.Source }
            });
        foreach (var r in set.Roads)
            features.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["geometry"] = new JsonObject { ["type"] = "LineString", ["coordinates"] = new JsonArray(r.Points.Select(p => (JsonNode)new JsonArray(p.Lon, p.Lat)).ToArray()) },
                ["properties"] = new JsonObject { ["name"] = r.Name, ["kind"] = "Road", ["category"] = r.Category }
            });
        return new JsonObject { ["type"] = "FeatureCollection", ["features"] = features }.ToJsonString();
    }

    public async Task<(int Points, int Roads, int Buildings)> SaveUploadAsync(string geoJson, string fileName, CancellationToken ct)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(geoJson); }
        catch (JsonException ex) { throw new BusinessRuleException($"Not JSON: {ex.Message}"); }
        if (node?["type"]?.GetValue<string>() != "FeatureCollection")
            throw new BusinessRuleException("Expected a GeoJSON FeatureCollection.");
        var set = ParseGeoJson(node);
        string name = Path.GetFileNameWithoutExtension(fileName);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        await File.WriteAllTextAsync(Path.Combine(_paths.Exposure, $"{name}.geojson"), geoJson, ct);
        return (set.Points.Count, set.Roads.Count, set.Buildings.Count);
    }

    public ExposureSet LoadLocal(BBox box, List<string>? sources = null)
    {
        var set = new ExposureSet();
        foreach (var file in Directory.GetFiles(_paths.Exposure, "*.*json"))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(file));
                var part = node?["type"]?.GetValue<string>() == "FeatureCollection" ? ParseGeoJson(node!) : ParseSimple(node!);
                var inBox = part.Within(box);
                if (inBox.Points.Count + inBox.Roads.Count + inBox.Buildings.Count == 0) continue;
                set = set.Merge(inBox);
                sources?.Add($"{Path.GetFileName(file)}: {inBox.Points.Count} points, {inBox.Roads.Count} roads, {inBox.Buildings.Count} buildings");
            }
            catch (Exception ex)
            {
                _log.LogWarning("Bad exposure file {File}: {Msg}", file, ex.Message);
            }
        }
        return set;
    }

    private static ExposureSet ParseSimple(JsonNode node)
    {
        var set = new ExposureSet();
        foreach (var p in node["points"]?.AsArray() ?? new JsonArray())
        {
            if (p is null) continue;
            var kind = Enum.TryParse<AssetKind>(p["kind"]?.GetValue<string>(), true, out var k) ? k : AssetKind.Other;
            set.Points.Add(new ExposurePoint(p["name"]?.GetValue<string>() ?? "?", kind,
                new GeoPoint(p["lon"]!.GetValue<double>(), p["lat"]!.GetValue<double>()),
                p["population"]?.GetValue<int>() ?? 0, p["deckClearance"]?.GetValue<double>(), "local"));
        }
        foreach (var r in node["roads"]?.AsArray() ?? new JsonArray())
        {
            if (r is null) continue;
            var pts = r["coords"]!.AsArray().Select(c => new GeoPoint(c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToList();
            set.Roads.Add(new ExposureLine(r["name"]?.GetValue<string>() ?? "road", r["category"]?.GetValue<string>() ?? "road", pts));
        }
        return set;
    }

    /// <summary>
    /// GeoJSON with properties: name, kind (Village/Town/Hospital/School/Bridge/...), population, deck_clearance.
    /// Polygons are treated as building footprints (Google Open Buildings / MS footprints exports), lines as roads.
    /// </summary>
    public static ExposureSet ParseGeoJson(JsonNode fc)
    {
        var set = new ExposureSet();
        foreach (var f in fc["features"]?.AsArray() ?? new JsonArray())
        {
            if (f is null) continue;
            var geom = f["geometry"];
            var props = f["properties"];
            string type = geom?["type"]?.GetValue<string>() ?? "";
            string name = props?["name"]?.ToString() ?? "unnamed";
            switch (type)
            {
                case "Point":
                {
                    var c = geom!["coordinates"]!.AsArray();
                    string kindStr = props?["kind"]?.ToString() ?? "Village";
                    var kind = Enum.TryParse<AssetKind>(kindStr, true, out var k) ? k : AssetKind.Other;
                    int pop = int.TryParse(props?["population"]?.ToString(), out int pp) ? pp : 0;
                    double? deck = double.TryParse(props?["deck_clearance"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
                    set.Points.Add(new ExposurePoint(name, kind, new GeoPoint(c[0]!.GetValue<double>(), c[1]!.GetValue<double>()), pop, deck, "upload"));
                    break;
                }
                case "LineString":
                {
                    var pts = geom!["coordinates"]!.AsArray().Select(c => new GeoPoint(c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToList();
                    set.Roads.Add(new ExposureLine(name, props?["category"]?.ToString() ?? "road", pts));
                    break;
                }
                case "Polygon":
                {
                    var ring = geom!["coordinates"]![0]!.AsArray().Select(c => new GeoPoint(c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToList();
                    double area = props?["area_in_meters"] is { } a && double.TryParse(a.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double am)
                        ? am : RasterOps.RingAreaM2(ring);
                    var centroid = new GeoPoint(ring.Average(p => p.Lon), ring.Average(p => p.Lat));
                    string use = props?["use"]?.ToString() ?? "residential";
                    set.Buildings.Add(new BuildingFootprint(centroid, area,
                        use.StartsWith("comm", StringComparison.OrdinalIgnoreCase) ? LandUse.Commercial :
                        use.StartsWith("ind", StringComparison.OrdinalIgnoreCase) ? LandUse.Industrial : LandUse.Residential));
                    break;
                }
            }
        }
        return set;
    }

    private async Task<ExposureSet> FetchOsmAsync(BBox box, CancellationToken ct)
    {
        string key = string.Create(CultureInfo.InvariantCulture, $"{box.South:F3}_{box.West:F3}_{box.North:F3}_{box.East:F3}");
        string cache = Path.Combine(_paths.Osm, key + ".json");
        string json;
        if (File.Exists(cache))
        {
            json = await File.ReadAllTextAsync(cache, ct);
        }
        else
        {
            if (_opt.Offline) return new ExposureSet();
            string bb = string.Create(CultureInfo.InvariantCulture, $"{box.South},{box.West},{box.North},{box.East}");
            string q = $"""
[out:json][timeout:90];
(
  node["place"~"^(city|town|village|hamlet|suburb)$"]({bb});
  node["amenity"~"^(hospital|clinic|school|police)$"]({bb});
  way["amenity"~"^(hospital|school)$"]({bb});
  way["highway"~"^(trunk|primary|secondary|tertiary)$"]["bridge"="yes"]({bb});
  way["highway"~"^(trunk|primary|secondary)$"]({bb});
  node["aeroway"="helipad"]({bb});
);
out tags geom;
""";
            var client = _http.CreateClient("overpass");
            using var resp = await client.PostAsync(_opt.OverpassUrl, new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = q }), ct);
            resp.EnsureSuccessStatusCode();
            json = await resp.Content.ReadAsStringAsync(ct);
            await File.WriteAllTextAsync(cache, json, ct);
        }
        return ParseOverpass(json);
    }

    public static ExposureSet ParseOverpass(string json)
    {
        var set = new ExposureSet();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("elements", out var elements)) return set;
        foreach (var e in elements.EnumerateArray())
        {
            var tags = e.TryGetProperty("tags", out var t) ? t : default;
            string Tag(string k) => tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            string type = e.GetProperty("type").GetString() ?? "";
            long id = e.GetProperty("id").GetInt64();
            List<GeoPoint> geom = new();
            if (type == "node")
                geom.Add(new GeoPoint(e.GetProperty("lon").GetDouble(), e.GetProperty("lat").GetDouble()));
            else if (e.TryGetProperty("geometry", out var g))
                foreach (var p in g.EnumerateArray())
                    geom.Add(new GeoPoint(p.GetProperty("lon").GetDouble(), p.GetProperty("lat").GetDouble()));
            if (geom.Count == 0) continue;
            var center = new GeoPoint(geom.Average(p => p.Lon), geom.Average(p => p.Lat));
            string name = Tag("name");
            if (string.IsNullOrEmpty(name)) name = Tag("name:en");

            string place = Tag("place"), amenity = Tag("amenity"), highway = Tag("highway");
            if (!string.IsNullOrEmpty(place))
            {
                int pop = int.TryParse(Tag("population").Replace(",", ""), out int pp) ? pp : PopulationByClass.GetValueOrDefault(place, 500);
                var kind = place is "town" or "city" ? AssetKind.Town : AssetKind.Village;
                set.Points.Add(new ExposurePoint(string.IsNullOrEmpty(name) ? $"{place} {id}" : name, kind, center, pop, null, "OSM"));
            }
            else if (amenity is "hospital" or "clinic")
                set.Points.Add(new ExposurePoint(string.IsNullOrEmpty(name) ? "Health facility" : name, AssetKind.Hospital, center, 0, null, "OSM"));
            else if (amenity == "school")
                set.Points.Add(new ExposurePoint(string.IsNullOrEmpty(name) ? "School" : name, AssetKind.School, center, 0, null, "OSM"));
            else if (amenity == "police")
                set.Points.Add(new ExposurePoint(string.IsNullOrEmpty(name) ? "Police" : name, AssetKind.PoliceStation, center, 0, null, "OSM"));
            else if (Tag("aeroway") == "helipad")
                set.Points.Add(new ExposurePoint(string.IsNullOrEmpty(name) ? "Helipad" : name, AssetKind.Helipad, center, 0, null, "OSM"));
            else if (!string.IsNullOrEmpty(highway) && Tag("bridge") == "yes")
            {
                string ref_ = Tag("ref");
                string bname = !string.IsNullOrEmpty(name) ? name : !string.IsNullOrEmpty(ref_) ? $"Bridge on {ref_}" : $"Bridge {id}";
                set.Points.Add(new ExposurePoint(bname, AssetKind.Bridge, geom[geom.Count / 2], 0, null, "OSM"));
            }
            else if (!string.IsNullOrEmpty(highway) && geom.Count > 1)
            {
                string rn = !string.IsNullOrEmpty(Tag("ref")) ? Tag("ref") : string.IsNullOrEmpty(name) ? highway : name;
                set.Roads.Add(new ExposureLine(rn, highway, geom));
            }
        }
        return set;
    }
}
