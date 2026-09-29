using PralayDrishti.Infrastructure.Engine.Geo;

namespace PralayDrishti.Infrastructure.Engine.Analysis;

public enum AssetKind { Village, Town, Hospital, School, Bridge, PowerHouse, Barrage, ArmyCamp, Helipad, PoliceStation, Other }

public sealed record ExposurePoint(
    string Name,
    AssetKind Kind,
    GeoPoint Location,
    int Population = 0,
    double? DeckClearance = null,
    string? Source = null);

public sealed record ExposureLine(string Name, string Category, List<GeoPoint> Points);

public sealed record BuildingFootprint(GeoPoint Centroid, double AreaM2, LandUse Use = LandUse.Residential);

public sealed class ExposureSet
{
    public List<ExposurePoint> Points { get; init; } = new();
    public List<ExposureLine> Roads { get; init; } = new();
    public List<BuildingFootprint> Buildings { get; init; } = new();

    public IEnumerable<ExposurePoint> Settlements => Points.Where(p => p.Kind is AssetKind.Village or AssetKind.Town);

    public ExposureSet Within(BBox box) => new()
    {
        Points = Points.Where(p => box.Contains(p.Location)).ToList(),
        Roads = Roads.Where(r => r.Points.Any(box.Contains)).ToList(),
        Buildings = Buildings.Where(b => box.Contains(b.Centroid)).ToList()
    };

    public ExposureSet Merge(ExposureSet other)
    {
        var names = new HashSet<string>(Points.Select(p => $"{p.Kind}:{p.Name}"));
        return new ExposureSet
        {
            Points = Points.Concat(other.Points.Where(p => !names.Contains($"{p.Kind}:{p.Name}"))).ToList(),
            Roads = Roads.Concat(other.Roads).ToList(),
            Buildings = Buildings.Concat(other.Buildings).ToList()
        };
    }
}
