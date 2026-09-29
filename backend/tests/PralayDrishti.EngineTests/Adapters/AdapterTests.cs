using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Infrastructure.Services;

namespace PralayDrishti.EngineTests.Adapters;

public class AdapterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pd_adapter_" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _sp;
    private readonly FloodSimulationEngine _engine;
    private readonly ResultStore _store;

    public AdapterTests()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        _sp = services.BuildServiceProvider();
        var opt = new AppOptions { Offline = true, UseOsm = false };
        var paths = new DataPaths(_dir);
        var http = _sp.GetRequiredService<IHttpClientFactory>();
        _store = new ResultStore(paths);
        _engine = new FloodSimulationEngine(
            new DemProvider(paths, opt, http, NullLogger<DemProvider>.Instance),
            new ExposureProvider(paths, opt, http, NullLogger<ExposureProvider>.Instance),
            _store, paths, NullLogger<FloodSimulationEngine>.Instance);
    }

    private static ScenarioSpec SmallSpec() => new()
    {
        Name = "adapter test",
        Dam = new DamSpec { Name = "T", Lon = 80.0, Lat = 25.0, HeightM = 30, ReservoirVolumeM3 = 10e6, Type = DamType.Earthfill },
        DurationHours = 0.5, RunSph = false, MaxCells = 40_000, DownstreamKm = 8, UpstreamKm = 2
    };

    [Fact]
    public async Task Engine_runs_synthetic_valley_and_reports_numbers()
    {
        var id = Guid.NewGuid();
        var progress = new List<string>();
        var r = await _engine.RunAsync(id, SmallSpec(), new Progress<(string Stage, double Fraction)>(p => progress.Add(p.Stage)), default);

        Assert.True(r.Numbers.FloodedAreaKm2 > 0);
        Assert.True(r.Numbers.SyntheticDem);   // offline, no cached tiles -> labelled synthetic
        Assert.True(r.Numbers.PeakDischarge > 0);
        Assert.True(_store.Exists(id));
        Assert.Contains(r.Log, l => l.Contains("SYNTHETIC"));
    }

    [Fact]
    public async Task Result_store_detail_roundtrip()
    {
        var id = Guid.NewGuid();
        var r = await _engine.RunAsync(id, SmallSpec(), new Progress<(string, double)>(), default);
        var summary = new RunSummaryDto(id, "x", RunStatus.Completed, 1, "Done", DateTime.UtcNow, DateTime.UtcNow, "DamBreak",
            RunOrigin.User, null, null, r.Numbers);

        var payload = await _store.GetDetailAsync(id, summary);
        Assert.NotNull(payload);
        using var doc = JsonDocument.Parse(payload!.Json);
        var root = doc.RootElement;
        Assert.Equal(id, root.GetProperty("summary").GetProperty("id").GetGuid());
        double peak = root.GetProperty("hydrograph").GetProperty("v").EnumerateArray().Max(e => e.GetDouble());
        Assert.Equal(r.Numbers.PeakDischarge, peak, 0);
        Assert.True(root.GetProperty("impact").GetProperty("floodedAreaKm2").GetDouble() > 0);
    }

    [Fact]
    public void Run_detail_with_nan_serialises()
    {
        // arrival/peak fields are NaN where the flood never reaches; STJ throws on NaN by default,
        // which would turn GET /api/runs/{id} into a 500
        var series = new SeriesDto(new[] { 0.0, 60.0 }, new[] { double.NaN, 1.5 });

        string json = JsonSerializer.Serialize(series, ResultStore.Json);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("NaN", doc.RootElement.GetProperty("v")[0].GetString());
        Assert.True(double.IsNaN(JsonSerializer.Deserialize<SeriesDto>(json, ResultStore.Json)!.V[0]));
    }

    public void Dispose()
    {
        _sp.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* temp */ }
    }
}
