using PralayDrishti.Application.Common.Models;
using PralayDrishti.Infrastructure.Engine.Geo;

namespace PralayDrishti.Infrastructure.Engine.Pipeline;

public static class ScenarioSpecExtensions
{
    public static GeoPoint Location(this DamSpec d) => new(d.Lon, d.Lat);

    public static List<GeoPoint> Ring(this NaturalDamSpec n) => n.DebrisPolygon.Select(p => new GeoPoint(p[0], p[1])).ToList();
}
