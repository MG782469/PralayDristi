using System.Text.Json;
using System.Text.Json.Nodes;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Infrastructure.Engine.Geo;

namespace PralayDrishti.Infrastructure.Services;

internal static class GeoJsonMask
{
    private static readonly JsonSerializerOptions GeoOpts = new() { Converters = { new GeoJsonConverterFactory() } };

    public static JsonNode? ToNode(Geometry? g) => g == null ? null : JsonNode.Parse(JsonSerializer.Serialize(g, GeoOpts));

    /// <summary>GeoJSON (FeatureCollection / Feature / Polygon / MultiPolygon) to a cell mask on the grid.</summary>
    public static bool[] Rasterize(DemGrid g, string geoJson)
    {
        JsonObject node;
        try
        {
            node = JsonNode.Parse(geoJson)?.AsObject() ?? throw new BusinessRuleException("Empty GeoJSON.");
        }
        catch (JsonException ex)
        {
            throw new BusinessRuleException($"Not valid GeoJSON: {ex.Message}");
        }

        var mask = new bool[g.Count];
        void Polygon(JsonArray rings)
        {
            var outer = rings[0]!.AsArray().Select(c => new GeoPoint(c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToList();
            var m = RasterOps.PolygonMask(g, outer);
            for (int k = 1; k < rings.Count; k++)
            {
                var hole = rings[k]!.AsArray().Select(c => new GeoPoint(c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToList();
                var hm = RasterOps.PolygonMask(g, hole);
                for (int i = 0; i < m.Length; i++) if (hm[i]) m[i] = false;
            }
            for (int i = 0; i < m.Length; i++) if (m[i]) mask[i] = true;
        }
        void Geometry(JsonNode? geom)
        {
            if (geom == null) return;
            switch (geom["type"]?.GetValue<string>())
            {
                case "Polygon": Polygon(geom["coordinates"]!.AsArray()); break;
                case "MultiPolygon": foreach (var p in geom["coordinates"]!.AsArray()) Polygon(p!.AsArray()); break;
                case "GeometryCollection": foreach (var gg in geom["geometries"]!.AsArray()) Geometry(gg); break;
            }
        }
        switch (node["type"]?.GetValue<string>())
        {
            case "FeatureCollection": foreach (var f in node["features"]!.AsArray()) Geometry(f?["geometry"]); break;
            case "Feature": Geometry(node["geometry"]); break;
            default: Geometry(node); break;
        }
        return mask;
    }
}
