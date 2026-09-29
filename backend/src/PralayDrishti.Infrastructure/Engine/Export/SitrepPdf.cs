using PralayDrishti.Infrastructure.Engine.Analysis;
using PralayDrishti.Infrastructure.Engine.Geo;
using PralayDrishti.Infrastructure.Engine.Pipeline;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp.PixelFormats;

namespace PralayDrishti.Infrastructure.Engine.Export;

/// <summary>1-click HADR situation report: what floods, when, who is at risk, where to stage relief.</summary>
public static class SitrepPdf
{
    static SitrepPdf() => QuestPDF.Settings.License = LicenseType.Community;

    public static byte[] Build(ScenarioOutcome o, string runId, DateTime generatedUtc)
    {
        var imp = o.Impact;
        var prep = o.Prepared;
        var g = o.FarField.Dem;
        var markers = imp.Places.Select(p => (new GeoPoint(p.Lon, p.Lat),
                p.HazardClass >= 5 ? new Rgba32(200, 0, 0, 255) : new Rgba32(255, 140, 0, 255)))
            .Concat(imp.Staging.Select(s => (new GeoPoint(s.Lon, s.Lat), new Rgba32(0, 170, 0, 255))))
            .Append((prep.DamPoint, new Rgba32(0, 0, 0, 255)));
        var mapPng = RasterRenderer.RenderMap(g, o.FarField.MaxDepth, LayerKind.Depth, markers, prep.ExcludeMask);
        var hazPng = RasterRenderer.RenderMap(g, o.HazardClass.Select(b => (float)b).ToArray(), LayerKind.Hazard,
            Array.Empty<(GeoPoint, Rgba32)>(), prep.ExcludeMask, 900);
        string title = prep.Spec.Mode == ScenarioMode.NaturalDam ? "Landslide lake outburst" : $"{prep.Spec.Dam.Name} - dam break";

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text($"SITREP - {title}").FontSize(16).Bold();
                        r.ConstantItem(170).AlignRight().Text($"Run {runId}\n{generatedUtc:dd MMM yyyy HH:mm} UTC").FontSize(8);
                    });
                    col.Item().Text("PralayDrishti dam-break decision support | MODELLED SCENARIO - verify before action").FontSize(8).Italic().FontColor(Colors.Red.Darken2);
                    col.Item().PaddingTop(4).LineHorizontal(1);
                });

                page.Content().PaddingTop(6).Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Row(r =>
                    {
                        Stat(r, "Population at risk", imp.PopulationAtRisk.ToString("N0"));
                        Stat(r, "Settlements", imp.SettlementsAffected.ToString());
                        Stat(r, "Flooded area", $"{imp.FloodedAreaKm2:F1} km2");
                        Stat(r, "First arrival", imp.Places.FirstOrDefault()?.ArrivalMin is double a ? $"{a:F0} min" : "-");
                        Stat(r, "Peak outflow", $"{prep.Hydrograph.Peak:N0} m3/s");
                        Stat(r, "Est. loss", $"Rs {imp.Loss.TotalCrore:N0} cr");
                    });

                    col.Item().Text("Recommended actions").FontSize(11).Bold();
                    foreach (var rec in imp.Recommendations)
                        col.Item().Text($"- {rec}");

                    col.Item().MaxHeight(430).AlignCenter().Image(mapPng).FitArea();
                    col.Item().Text("Maximum flood depth. Red = H5/H6 settlements, orange = other settlements, green = staging/HLZ, black = dam.").FontSize(7).Italic();

                    col.Item().Text("Settlements (ordered by flood arrival)").FontSize(11).Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3); c.RelativeColumn(1.4f); c.RelativeColumn(1.4f); c.RelativeColumn(1.4f);
                            c.RelativeColumn(1.4f); c.RelativeColumn(1); c.RelativeColumn(1.6f); c.RelativeColumn(4);
                        });
                        t.Header(h =>
                        {
                            foreach (var s in new[] { "Name", "Pop.", "Arrival min", "Depth m", "Vel m/s", "Hazard", "Golden min", "Action" })
                                h.Cell().Background(Colors.Grey.Lighten2).Padding(2).Text(s).Bold();
                        });
                        foreach (var p in imp.Places.Take(40))
                        {
                            var bg = p.GoldenTimeMin is < 0 ? Colors.Red.Lighten4 : p.GoldenTimeMin is < 15 ? Colors.Orange.Lighten4 : Colors.White;
                            t.Cell().Background(bg).Padding(2).Text(p.Name);
                            t.Cell().Background(bg).Padding(2).Text(p.Population.ToString("N0"));
                            t.Cell().Background(bg).Padding(2).Text($"{p.ArrivalMin:F0}");
                            t.Cell().Background(bg).Padding(2).Text($"{p.MaxDepth:F1}");
                            t.Cell().Background(bg).Padding(2).Text($"{p.MaxSpeed:F1}");
                            t.Cell().Background(bg).Padding(2).Text(p.HazardLabel);
                            t.Cell().Background(bg).Padding(2).Text($"{p.GoldenTimeMin:F0}");
                            t.Cell().Background(bg).Padding(2).Text(p.Advice).FontSize(7);
                        }
                    });

                    if (imp.Bridges.Count > 0)
                    {
                        col.Item().Text("Bridges (Army / NDRF movement)").FontSize(11).Bold();
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1.5f); c.RelativeColumn(1.5f); c.RelativeColumn(1.5f); c.RelativeColumn(1.5f); });
                            t.Header(h =>
                            {
                                foreach (var s in new[] { "Bridge", "Status", "Max WS m", "Deck m", "Overtop min" })
                                    h.Cell().Background(Colors.Grey.Lighten2).Padding(2).Text(s).Bold();
                            });
                            foreach (var b in imp.Bridges)
                            {
                                t.Cell().Padding(2).Text(b.Name);
                                t.Cell().Padding(2).Text(b.Overtopped ? "OVERTOPPED" : "clear").FontColor(b.Overtopped ? Colors.Red.Darken2 : Colors.Green.Darken2);
                                t.Cell().Padding(2).Text($"{b.MaxWaterSurface:F1}");
                                t.Cell().Padding(2).Text($"{b.DeckLevel:F1}");
                                t.Cell().Padding(2).Text($"{b.OvertopMin:F0}");
                            }
                        });
                    }

                    var hosp = imp.Places.Where(p => p.Kind is "Hospital" or "School").ToList();
                    if (hosp.Count > 0)
                    {
                        col.Item().Text("Hospitals and schools in the flood zone").FontSize(11).Bold();
                        foreach (var h in hosp)
                            col.Item().Text($"- {h.Kind}: {h.Name} - {h.MaxDepth:F1} m at {h.ArrivalMin:F0} min ({h.HazardLabel}). Evacuate / relocate patients before arrival.");
                    }

                    if (imp.Staging.Count > 0)
                    {
                        col.Item().Text("Relief staging areas and helicopter landing zones").FontSize(11).Bold();
                        foreach (var s in imp.Staging)
                            col.Item().Text($"- {s.Type}: {s.Lat:F4}N {s.Lon:F4}E, {s.AreaHa} ha, slope {s.MeanSlope} deg, {s.DistanceToFloodKm} km from flood edge, road access {(s.NearRoad ? "yes" : "no")}");
                    }

                    if (imp.Sensors.Count > 0)
                    {
                        col.Item().Text("Early-warning sensor placement").FontSize(11).Bold();
                        int k = 1;
                        foreach (var s in imp.Sensors)
                            col.Item().Text($"- S{k++} at {s.DistanceFromDamKm} km below dam ({s.Lat:F4}N {s.Lon:F4}E): " +
                                            string.Join(", ", s.LeadTimes.Take(5).Select(l => $"{l.Place} +{l.LeadMin:F0} min")));
                    }

                    col.Item().PageBreak();
                    col.Item().Text("Hazard classification (depth x velocity)").FontSize(11).Bold();
                    col.Item().MaxHeight(400).AlignCenter().Image(hazPng).FitArea();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(5); c.RelativeColumn(1.5f); });
                        for (int i = 1; i <= 6; i++)
                        {
                            t.Cell().Padding(2).Text($"H{i}");
                            t.Cell().Padding(2).Text(Hazard.Labels[i]);
                            t.Cell().Padding(2).Text($"{imp.HazardAreaKm2.ElementAtOrDefault(i):F2} km2");
                        }
                    });

                    col.Item().Text("Loss & damage (indicative)").FontSize(11).Bold();
                    col.Item().Text($"Residential Rs {imp.Loss.ResidentialInr / 1e7:N1} cr ({imp.Loss.BuildingsAffected:N0} buildings/households), " +
                                    $"crops Rs {imp.Loss.CropInr / 1e7:N1} cr ({imp.Loss.CropAreaHa:N0} ha), roads Rs {imp.Loss.RoadsInr / 1e7:N1} cr, " +
                                    $"bridges Rs {imp.Loss.BridgesInr / 1e7:N1} cr. Method: {imp.Loss.Method}.");

                    col.Item().Text("Scenario and model").FontSize(11).Bold();
                    col.Item().Text($"Breach: {prep.Breach.Method}; average width {prep.Breach.AverageWidth:F0} m, formation {prep.Breach.FormationTime / 60:F0} min, peak {prep.Hydrograph.Peak:N0} m3/s.");
                    foreach (var p in prep.EmpiricalPeaks)
                        col.Item().Text($"  cross-check {p.Method}: {p.PeakM3s:N0} m3/s");
                    col.Item().Text($"Far field: {o.EngineName}; grid {g.Cols} x {g.Rows} at {g.Dx:F0} m; mass balance error {o.FarField.MassError:P3}.");
                    if (o.NearField is { } nf)
                    {
                        col.Item().Text($"Near field: WCSPH 2D-vertical at 1:{nf.Scale:F0}, {nf.Particles} particles.");
                        if (nf.PeakImpactForceKN is double f)
                            col.Item().Text($"Peak impact on {nf.StructureName}: {f:N0} kN (mean pressure {nf.PeakImpactPressureKPa:N0} kPa).").Bold();
                    }
                    if (o.Ensemble is { } ens)
                        col.Item().Text($"Ensemble: {ens.Members} Monte Carlo members; flooded area {ens.FloodedAreaKm2.Min():F1}-{ens.FloodedAreaKm2.Max():F1} km2.");
                    foreach (var l in prep.Log) col.Item().Text($"  {l}").FontSize(7).FontColor(Colors.Grey.Darken2);

                    col.Item().Text("Limitations").FontSize(11).Bold();
                    col.Item().Text("30 m open DEM, no river bathymetry (nominal channel burnt in), empirical breach parameters, uniform roughness, " +
                                    "population from settlement points. Treat depths as +/- 1-2 m and arrival times as +/- 20%. See ensemble spread.");
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void Stat(RowDescriptor r, string label, string value)
    {
        r.RelativeItem().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4).Column(c =>
        {
            c.Item().Text(label).FontSize(7).FontColor(Colors.Grey.Darken2);
            c.Item().Text(value).FontSize(12).Bold();
        });
    }
}
