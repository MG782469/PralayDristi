using PralayDrishti.Core.Geo;
using PralayDrishti.Core.Solvers;

namespace PralayDrishti.Core.Analysis;

public sealed class ImpactOptions
{
    public double FloodDepthThreshold { get; init; } = 0.1;
    public double SettlementRadiusM { get; init; } = 150;
    public double WarningDisseminationMin { get; init; } = 15;   // time to get the word out after detection
    public double WalkingSpeed { get; init; } = 1.0;             // m/s, elderly/children included
    public double DefaultBridgeClearance { get; init; } = 10;
    public double PersonsPerHousehold { get; init; } = 4.8;
    public double FloorAreaPerHousehold { get; init; } = 45;
    public double FarmlandFraction { get; init; } = 0.35;        // share of flat flooded land under crops
    public double SensorTelemetryMin { get; init; } = 2;
    public DepthDamage Damage { get; init; } = new();
}

public sealed record AffectedPlace(
    string Name, string Kind, double Lon, double Lat, int Population,
    double MaxDepth, double MaxSpeed, double? ArrivalMin, double? PeakMin,
    int HazardClass, string HazardLabel, double DistanceFromDamKm,
    double? EvacuationMin, double? GoldenTimeMin, string Urgency, string Advice);

public sealed record BridgeStatus(
    string Name, double Lon, double Lat, bool Overtopped, double MaxWaterSurface, double DeckLevel,
    double? OvertopMin, double MaxSpeed);

public sealed record RoadCut(string Name, string Category, double FloodedKm, double? FirstCutMin, List<GeoPoint> CutPoints);

public sealed record StagingArea(
    string Id, string Type, double Lon, double Lat, double AreaHa, double MeanSlope, double Elevation,
    double DistanceToFloodKm, bool NearRoad, double Score);

public sealed record SensorSuggestion(double Lon, double Lat, double DistanceFromDamKm, double ArrivalMin,
    List<LeadTime> LeadTimes);

public sealed record LeadTime(string Place, double LeadMin);

public sealed record LossSummary(
    double ResidentialInr, double CropInr, double RoadsInr, double BridgesInr, double BuildingsAffected,
    double CropAreaHa, string Method)
{
    public double TotalInr => ResidentialInr + CropInr + RoadsInr + BridgesInr;
    public double TotalCrore => TotalInr / 1e7;
}

public sealed class ImpactReport
{
    public double FloodedAreaKm2 { get; init; }
    public double MaxDepth { get; init; }
    public double MaxSpeed { get; init; }
    public int PopulationAtRisk { get; init; }
    public int SettlementsAffected { get; init; }
    public double[] HazardAreaKm2 { get; init; } = Array.Empty<double>();
    public List<AffectedPlace> Places { get; init; } = new();
    public List<BridgeStatus> Bridges { get; init; } = new();
    public List<RoadCut> Roads { get; init; } = new();
    public List<StagingArea> Staging { get; init; } = new();
    public List<SensorSuggestion> Sensors { get; init; } = new();
    public List<string> Recommendations { get; init; } = new();
    public required LossSummary Loss { get; init; }
    public double? FloodFrontReachKm { get; init; }
}

public static class ImpactAnalyzer
{
    public static ImpactReport Analyze(SweResult res, ExposureSet exposure, GeoPoint dam,
        IReadOnlyList<int> riverPath, bool[]? exclude = null, ImpactOptions? opt = null)
    {
        opt ??= new ImpactOptions();
        var g = res.Dem;
        int n = g.Count;
        var wet = new bool[n];
        for (int i = 0; i < n; i++)
            wet[i] = res.MaxDepth[i] >= opt.FloodDepthThreshold && (exclude == null || !exclude[i]);

        double cellKm2 = g.CellArea / 1e6;
        var hazardArea = new double[7];
        for (int i = 0; i < n; i++)
            if (wet[i]) hazardArea[Hazard.Classify(res.MaxDepth[i], res.MaxSpeed[i])] += cellKm2;

        var distToDry = DistanceTransform(g, i => wet[i]);        // metres to nearest non-flooded cell
        var distToWet = DistanceTransform(g, i => !wet[i]);       // metres to nearest flooded cell

        var places = new List<AffectedPlace>();
        foreach (var p in exposure.Points.Where(p => p.Kind != AssetKind.Bridge))
        {
            if (!g.TryCell(p.Location, out int r, out int c)) continue;
            int rad = p.Kind is AssetKind.Village or AssetKind.Town
                ? Math.Max(0, (int)(opt.SettlementRadiusM / g.Dx)) : 0;
            int best = -1;
            for (int rr = r - rad; rr <= r + rad; rr++)
                for (int cc = c - rad; cc <= c + rad; cc++)
                {
                    if (!g.InBounds(rr, cc)) continue;
                    int k = g.Idx(rr, cc);
                    if (!wet[k]) continue;
                    if (best < 0 || res.MaxDepth[k] > res.MaxDepth[best]) best = k;
                }
            if (best < 0) continue;

            double depth = res.MaxDepth[best], speed = res.MaxSpeed[best];
            int cls = Hazard.Classify(depth, speed);
            double? arr = float.IsNaN(res.ArrivalTime[best]) ? null : res.ArrivalTime[best] / 60.0;
            double? peak = res.PeakTime[best] > 0 ? res.PeakTime[best] / 60.0 : null;
            double evac = distToDry[best] / opt.WalkingSpeed / 60.0;
            double? golden = arr is null ? null : arr - opt.WarningDisseminationMin - evac;
            string urgency = golden switch
            {
                null => "Monitor",
                < 0 => "CRITICAL - flood arrives before evacuation can finish",
                < 15 => "TIGHT - under 15 min margin",
                < 60 => "ACT NOW - under 1 h margin",
                _ => "PREPARE"
            };
            places.Add(new AffectedPlace(p.Name, p.Kind.ToString(), p.Location.Lon, p.Location.Lat, p.Population,
                Math.Round(depth, 2), Math.Round(speed, 2), Round(arr), Round(peak), cls, Hazard.ShortLabels[cls],
                Math.Round(dam.DistanceTo(p.Location) / 1000, 2), Math.Round(evac, 1), Round(golden), urgency,
                Hazard.Advice(cls)));
        }
        places = places.OrderBy(p => p.ArrivalMin ?? double.MaxValue).ToList();

        var bridges = new List<BridgeStatus>();
        foreach (var b in exposure.Points.Where(p => p.Kind == AssetKind.Bridge))
        {
            if (!g.TryCell(b.Location, out int r, out int c)) continue;
            // bridges sit on the channel; look for the lowest bed nearby to find the river under it
            int k = g.Idx(r, c);
            for (int rr = r - 2; rr <= r + 2; rr++)
                for (int cc = c - 2; cc <= c + 2; cc++)
                    if (g.InBounds(rr, cc) && g[rr, cc] < g.Z[k]) k = g.Idx(rr, cc);
            double deck = g.Z[k] + (b.DeckClearance ?? opt.DefaultBridgeClearance);
            double ws = float.IsNaN(res.MaxSurface[k]) ? g.Z[k] : res.MaxSurface[k];
            bool over = ws > deck;
            double? tOver = null;
            if (over && res.MaxDepth[k] > 0)
            {
                // estimate the overtopping time assuming a linear rise between arrival and peak
                double ta = float.IsNaN(res.ArrivalTime[k]) ? 0 : res.ArrivalTime[k];
                double tp = res.PeakTime[k];
                double need = deck - g.Z[k];
                tOver = (ta + (tp - ta) * Math.Clamp(need / res.MaxDepth[k], 0, 1)) / 60.0;
            }
            bridges.Add(new BridgeStatus(b.Name, b.Location.Lon, b.Location.Lat, over, Math.Round(ws, 1),
                Math.Round(deck, 1), Round(tOver), Math.Round(res.MaxSpeed[k], 1)));
        }

        var roads = new List<RoadCut>();
        foreach (var road in exposure.Roads)
        {
            double km = 0;
            double? first = null;
            var cuts = new List<GeoPoint>();
            for (int s = 1; s < road.Points.Count; s++)
            {
                var a = road.Points[s - 1];
                var b = road.Points[s];
                double len = a.DistanceTo(b);
                int steps = Math.Max(1, (int)(len / (g.Dx * 0.5)));
                for (int k = 0; k < steps; k++)
                {
                    double f = (k + 0.5) / steps;
                    var p = new GeoPoint(a.Lon + f * (b.Lon - a.Lon), a.Lat + f * (b.Lat - a.Lat));
                    var ci = g.CellIndex(p);
                    if (ci is not int idx || !wet[idx] || res.MaxDepth[idx] < 0.3) continue;
                    km += len / steps / 1000;
                    if (!float.IsNaN(res.ArrivalTime[idx]))
                        first = Math.Min(first ?? double.MaxValue, res.ArrivalTime[idx] / 60.0);
                    if (cuts.Count == 0 || cuts[^1].DistanceTo(p) > 500) cuts.Add(p);
                }
            }
            if (km > 0) roads.Add(new RoadCut(road.Name, road.Category, Math.Round(km, 2), Round(first), cuts));
        }

        var staging = FindStagingAreas(g, wet, distToWet, exposure, places);
        var sensors = SuggestSensors(res, riverPath, places, opt);
        var loss = EstimateLoss(res, wet, exposure, places, bridges, roads, opt);

        double reach = 0;
        foreach (int i in riverPath)
            if (wet[i]) reach = Math.Max(reach, dam.DistanceTo(g.CellCenter(i)));

        var recs = BuildRecommendations(places, bridges, roads, staging, sensors);

        return new ImpactReport
        {
            FloodedAreaKm2 = Math.Round(wet.Count(w => w) * cellKm2, 3),
            MaxDepth = Math.Round(Enumerable.Range(0, n).Where(i => wet[i]).Select(i => (double)res.MaxDepth[i]).DefaultIfEmpty().Max(), 2),
            MaxSpeed = Math.Round(Enumerable.Range(0, n).Where(i => wet[i]).Select(i => (double)res.MaxSpeed[i]).DefaultIfEmpty().Max(), 2),
            PopulationAtRisk = places.Sum(p => p.Population),
            SettlementsAffected = places.Count(p => p.Kind is "Village" or "Town"),
            HazardAreaKm2 = hazardArea.Select(a => Math.Round(a, 3)).ToArray(),
            Places = places,
            Bridges = bridges,
            Roads = roads,
            Staging = staging,
            Sensors = sensors,
            Recommendations = recs,
            Loss = loss,
            FloodFrontReachKm = Math.Round(reach / 1000, 2)
        };
    }

    private static double? Round(double? v) => v is null ? null : Math.Round(v.Value, 1);

    /// <summary>Chamfer distance (metres) from every cell to the nearest cell where target(i) is false.</summary>
    public static float[] DistanceTransform(DemGrid g, Func<int, bool> inside)
    {
        int n = g.Count;
        var d = new float[n];
        const float inf = 1e12f;
        for (int i = 0; i < n; i++) d[i] = inside(i) ? inf : 0;
        float dx = (float)g.Dx, dy = (float)g.Dy, dd = MathF.Sqrt(dx * dx + dy * dy);
        for (int r = 0; r < g.Rows; r++)
            for (int c = 0; c < g.Cols; c++)
            {
                int i = g.Idx(r, c);
                if (d[i] == 0) continue;
                if (c > 0) d[i] = Math.Min(d[i], d[i - 1] + dx);
                if (r > 0)
                {
                    d[i] = Math.Min(d[i], d[i - g.Cols] + dy);
                    if (c > 0) d[i] = Math.Min(d[i], d[i - g.Cols - 1] + dd);
                    if (c < g.Cols - 1) d[i] = Math.Min(d[i], d[i - g.Cols + 1] + dd);
                }
            }
        for (int r = g.Rows - 1; r >= 0; r--)
            for (int c = g.Cols - 1; c >= 0; c--)
            {
                int i = g.Idx(r, c);
                if (d[i] == 0) continue;
                if (c < g.Cols - 1) d[i] = Math.Min(d[i], d[i + 1] + dx);
                if (r < g.Rows - 1)
                {
                    d[i] = Math.Min(d[i], d[i + g.Cols] + dy);
                    if (c < g.Cols - 1) d[i] = Math.Min(d[i], d[i + g.Cols + 1] + dd);
                    if (c > 0) d[i] = Math.Min(d[i], d[i + g.Cols - 1] + dd);
                }
            }
        return d;
    }

    /// <summary>
    /// Relief staging areas and helicopter landing zones: dry, flat, not too far from the flood, ideally near a road.
    /// HLZ: slope under 5 deg, at least ~0.25 ha clear. Staging: slope under 8 deg, at least 2 ha.
    /// </summary>
    public static List<StagingArea> FindStagingAreas(DemGrid g, bool[] wet, float[] distToWet, ExposureSet exposure,
        List<AffectedPlace> places, int maxResults = 8)
    {
        var slope = g.Slope();
        var result = new List<StagingArea>();
        var affected = places.Where(p => p.Population > 0).ToList();

        foreach (var (type, maxSlope, minHa) in new[] { ("HLZ", 5.0, 0.25), ("Staging", 8.0, 2.0) })
        {
            var mask = new bool[g.Count];
            for (int i = 0; i < g.Count; i++)
                mask[i] = !wet[i] && slope[i] < maxSlope && distToWet[i] > 300 && distToWet[i] < 10_000;
            var (labels, count) = RasterOps.Label(g, mask);
            var stats = new Dictionary<int, (int cells, double sx, double sy, double sl, double el, double dist)>();
            for (int i = 0; i < g.Count; i++)
            {
                int l = labels[i];
                if (l == 0) continue;
                var p = g.CellCenter(i);
                stats.TryGetValue(l, out var s);
                stats[l] = (s.cells + 1, s.sx + p.Lon, s.sy + p.Lat, s.sl + slope[i], s.el + g.Z[i], s.dist + distToWet[i]);
            }
            double cellHa = g.CellArea / 1e4;
            var candidates = new List<StagingArea>();
            foreach (var (l, s) in stats)
            {
                double ha = s.cells * cellHa;
                if (ha < minHa) continue;
                var c = new GeoPoint(s.sx / s.cells, s.sy / s.cells);
                bool nearRoad = exposure.Roads.Any(rd => RasterOps.DistanceToPolyline(c, rd.Points) < 1000);
                double distFlood = s.dist / s.cells / 1000;
                double toPeople = affected.Count == 0 ? 5 : affected.Min(p => c.DistanceTo(new GeoPoint(p.Lon, p.Lat))) / 1000;
                double score = Math.Min(ha, 50) / 50 * 40 + (nearRoad ? 25 : 0) + Math.Max(0, 20 - toPeople * 3) +
                               Math.Max(0, 15 - s.sl / s.cells * 2);
                candidates.Add(new StagingArea($"{type}-{l}", type, Math.Round(c.Lon, 5), Math.Round(c.Lat, 5),
                    Math.Round(ha, 2), Math.Round(s.sl / s.cells, 1), Math.Round(s.el / s.cells, 0),
                    Math.Round(distFlood, 2), nearRoad, Math.Round(score, 1)));
            }
            result.AddRange(candidates.OrderByDescending(c => c.Score).Take(maxResults / 2));
        }
        return result;
    }

    /// <summary>
    /// Water level sensor sites along the river: every ~2 km below the dam we know when the flood gets there,
    /// so for each downstream settlement lead = arrival(place) - arrival(sensor) - telemetry.
    /// </summary>
    public static List<SensorSuggestion> SuggestSensors(SweResult res, IReadOnlyList<int> path,
        List<AffectedPlace> places, ImpactOptions opt, int count = 3)
    {
        var g = res.Dem;
        var list = new List<SensorSuggestion>();
        if (path.Count < 3) return list;
        var candidates = new List<(int cell, double chain, double arr)>();
        double chain = 0, lastPick = double.MinValue;
        for (int k = 0; k < path.Count; k++)
        {
            if (k > 0) chain += g.CellCenter(path[k - 1]).DistanceTo(g.CellCenter(path[k]));
            float a = res.ArrivalTime[path[k]];
            if (float.IsNaN(a) || chain < 300) continue;
            if (chain - lastPick >= 2000)
            {
                candidates.Add((path[k], chain, a / 60.0));
                lastPick = chain;
            }
        }
        // the first one is always the most valuable (earliest detection); spread the rest out
        var picks = new List<(int cell, double chain, double arr)>();
        if (candidates.Count > 0) picks.Add(candidates[0]);
        for (int k = 1; k < count && candidates.Count > 1; k++)
        {
            int idx = (int)Math.Round((double)k * (candidates.Count - 1) / count);
            if (!picks.Contains(candidates[idx])) picks.Add(candidates[idx]);
        }

        foreach (var s in picks)
        {
            var leads = places.Where(p => p.ArrivalMin is double a && a > s.arr)
                .Select(p => new LeadTime(p.Name, Math.Round(p.ArrivalMin!.Value - s.arr - opt.SensorTelemetryMin, 1)))
                .Where(x => x.LeadMin > 0)
                .OrderByDescending(x => x.LeadMin)
                .Take(10)
                .ToList();
            var pt = g.CellCenter(s.cell);
            list.Add(new SensorSuggestion(Math.Round(pt.Lon, 5), Math.Round(pt.Lat, 5), Math.Round(s.chain / 1000, 2),
                Math.Round(s.arr, 1), leads));
        }
        return list;
    }

    private static LossSummary EstimateLoss(SweResult res, bool[] wet, ExposureSet exposure,
        List<AffectedPlace> places, List<BridgeStatus> bridges, List<RoadCut> roads, ImpactOptions opt)
    {
        var dmg = opt.Damage;
        var g = res.Dem;
        double resid = 0, buildings = 0;
        string method;
        if (exposure.Buildings.Count > 0)
        {
            method = "Building footprints x JRC depth-damage (Asia)";
            foreach (var b in exposure.Buildings)
            {
                var ci = g.CellIndex(b.Centroid);
                if (ci is not int i || !wet[i]) continue;
                resid += dmg.BuildingLoss(b.Use, res.MaxDepth[i], b.AreaM2);
                buildings++;
            }
        }
        else
        {
            method = "Settlement population -> households -> floor area x JRC depth-damage (Asia)";
            foreach (var p in places.Where(p => p.Population > 0))
            {
                double hh = p.Population / opt.PersonsPerHousehold;
                resid += dmg.BuildingLoss(LandUse.Residential, p.MaxDepth, hh * opt.FloorAreaPerHousehold);
                buildings += hh;
            }
        }

        var slope = g.Slope();
        double cropHa = 0, crop = 0, cellHa = g.CellArea / 1e4;
        for (int i = 0; i < g.Count; i++)
        {
            if (!wet[i] || slope[i] > 4) continue;
            double ha = cellHa * opt.FarmlandFraction;
            cropHa += ha;
            crop += dmg.CropLoss(res.MaxDepth[i], ha);
        }

        double roadLoss = roads.Sum(r => r.FloodedKm) * dmg.RoadInrPerKm * 0.5;
        double bridgeLoss = bridges.Count(b => b.Overtopped) * dmg.BridgeInr * 0.6;
        return new LossSummary(Math.Round(resid), Math.Round(crop), Math.Round(roadLoss), Math.Round(bridgeLoss),
            Math.Round(buildings), Math.Round(cropHa, 1), method);
    }

    private static List<string> BuildRecommendations(List<AffectedPlace> places, List<BridgeStatus> bridges,
        List<RoadCut> roads, List<StagingArea> staging, List<SensorSuggestion> sensors)
    {
        var recs = new List<string>();
        var critical = places.Where(p => p.GoldenTimeMin is < 15).ToList();
        if (critical.Count > 0)
            recs.Add($"{critical.Count} settlement(s) have less than 15 min golden time ({string.Join(", ", critical.Take(6).Select(p => p.Name))}). Pre-install sirens with automatic trigger and pre-identify high ground.");
        var high = places.Where(p => p.HazardClass >= 5).ToList();
        if (high.Count > 0)
            recs.Add($"Structural failure risk (H5/H6) at {string.Join(", ", high.Take(6).Select(p => p.Name))} - full evacuation, no shelter-in-place.");
        var over = bridges.Where(b => b.Overtopped).OrderBy(b => b.OvertopMin ?? 1e9).ToList();
        if (over.Count > 0)
            recs.Add($"{over.Count} bridge(s) will be overtopped - first: {over[0].Name} at ~{over[0].OvertopMin:F0} min. Stop traffic and plan Army/NDRF movement via alternate routes.");
        if (roads.Count > 0)
            recs.Add($"{roads.Sum(r => r.FloodedKm):F1} km of road will be cut. Earliest cut ~{roads.Min(r => r.FirstCutMin ?? 1e9):F0} min.");
        var hlz = staging.Where(s => s.Type == "HLZ").OrderByDescending(s => s.Score).FirstOrDefault();
        if (hlz != null)
            recs.Add($"Best helicopter landing zone: {hlz.Lat:F4}N {hlz.Lon:F4}E ({hlz.AreaHa} ha, slope {hlz.MeanSlope} deg{(hlz.NearRoad ? ", road access" : "")}).");
        if (sensors.Count > 0 && sensors[0].LeadTimes.Count > 0)
        {
            var best = sensors[0].LeadTimes[0];
            recs.Add($"A water-level sensor {sensors[0].DistanceFromDamKm:F1} km below the dam gives {best.Place} about {best.LeadMin:F0} min lead time.");
        }
        return recs;
    }
}
