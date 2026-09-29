using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace PralayDrishti.EngineTests.Integration;

/// <summary>
/// End-to-end through HTTP against the docker-compose.dev.yml Postgres and Redis.
/// Skipped automatically when they are not running.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Db = "Host=localhost;Port=5432;Database=pralaydrishti_api_test;Username=pralay;Password=pralay";
    public const string AnalystEmail = "analyst@test.local";
    public const string AdminEmail = "admin@test.local";
    public const string DemoPassword = "Demo@12345";
    private readonly string _data = Path.Combine(Path.GetTempPath(), "pd_api_" + Guid.NewGuid().ToString("N"));

    public ApiFactory()
    {
        // fresh database per test run
        NpgsqlConnection.ClearAllPools();
        using var db = new PralayDrishti.Infrastructure.Persistence.AppDbContext(
            new DbContextOptionsBuilder<PralayDrishti.Infrastructure.Persistence.AppDbContext>().UseNpgsql(Db).UseSnakeCaseNamingConvention().Options);
        db.Database.EnsureDeleted();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting, not ConfigureAppConfiguration: Program reads configuration before Build()
        builder.UseSetting("ConnectionStrings:DefaultConnection", Db);
        builder.UseSetting("ConnectionStrings:Redis", "localhost:6379");
        builder.UseSetting("Jwt:Secret", new string('t', 48));
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("PralayDrishti:DataDir", _data);
        builder.UseSetting("PralayDrishti:Offline", "true");
        builder.UseSetting("PralayDrishti:UseOsm", "false");
        builder.UseSetting("PralayDrishti:LakeWatch:Enabled", "false");
        builder.UseSetting("Seed:DemoUsers:0:Email", AdminEmail);
        builder.UseSetting("Seed:DemoUsers:0:Password", DemoPassword);
        builder.UseSetting("Seed:DemoUsers:0:Role", "Admin");
        builder.UseSetting("Seed:DemoUsers:1:Email", AnalystEmail);
        builder.UseSetting("Seed:DemoUsers:1:Password", DemoPassword);
        builder.UseSetting("Seed:DemoUsers:1:Role", "Analyst");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_data, true); } catch { /* temp */ }
    }
}

public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!PostgresFactAttribute.Reachable("localhost", 5432) || !PostgresFactAttribute.Reachable("localhost", 6379))
            Skip = "Postgres/Redis not reachable (start docker-compose.dev.yml)";
    }
}

[CollectionDefinition("api", DisableParallelization = true)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory> { }

[Collection("api")]
public class ApiTests
{
    private readonly ApiFactory _factory;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ApiTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAs(string email, string password = ApiFactory.DemoPassword)
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/auth/login", new { email, password });
        r.EnsureSuccessStatusCode();
        var token = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [DockerFact]
    public async Task Swagger_declares_bearer_scheme()
    {
        var json = await _factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("\"Bearer\"", json);
        Assert.Contains("bearer", json);
    }

    [DockerFact]
    public async Task Anonymous_get_dams_is_401()
    {
        var r = await _factory.CreateClient().GetAsync("/api/dams");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [DockerFact]
    public async Task Viewer_cannot_start_run_403()
    {
        var c = _factory.CreateClient();
        var reg = await c.PostAsJsonAsync("/api/auth/register",
            new { email = $"v{Guid.NewGuid():N}@test.local", password = "Viewer123", fullName = "Viewer" });
        reg.EnsureSuccessStatusCode();
        var token = (await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/dams")).StatusCode);
        var r = await c.PostAsJsonAsync("/api/runs", new { });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [DockerFact]
    public async Task Validation_error_returns_problem_details_400_with_errors()
    {
        var r = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", password = "x", fullName = "" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").EnumerateObject().Any());
    }

    [DockerFact]
    public async Task Wms_getcapabilities_is_anonymous()
    {
        var r = await _factory.CreateClient().GetAsync("/wms?SERVICE=WMS&REQUEST=GetCapabilities");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("WMS_Capabilities", await r.Content.ReadAsStringAsync());
    }

    [DockerFact]
    public async Task Refresh_rotates_and_old_token_stops_working_after_grace()
    {
        var c = _factory.CreateClient();
        var login = await (await c.PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.AnalystEmail, password = ApiFactory.DemoPassword }))
            .Content.ReadFromJsonAsync<JsonElement>();
        string rt = login.GetProperty("refreshToken").GetString()!;
        var refreshed = await c.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = rt });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.NotEqual(rt, (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString());
    }

    [DockerFact]
    public async Task Refresh_cookie_is_secure_even_behind_a_tls_terminating_proxy()
    {
        // the test server speaks plain http, exactly like Kestrel behind a PaaS load balancer
        var r = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AnalystEmail, password = ApiFactory.DemoPassword });

        var cookie = r.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pd_refresh="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [DockerFact]
    public async Task Analyst_full_flow_register_login_run_export_pdf()
    {
        var c = await ClientAs(ApiFactory.AnalystEmail);
        var spec = new
        {
            name = "api smoke",
            mode = "DamBreak",
            dam = new { name = "Smoke", lon = 80.0, lat = 25.0, type = "Earthfill", heightM = 30, reservoirVolumeM3 = 1e7, crestLengthM = 300, crestWidthM = 10, failureMode = "Overtopping", storageExponent = 2.5 },
            downstreamDams = Array.Empty<object>(),
            durationHours = 0.5, manning = 0.045, channelManning = 0.035, channelBurnM = 1.5, maxCells = 40000,
            downstreamKm = 8, upstreamKm = 2, engine = "Internal", runSph = false, sphHandover = false, sphResolution = 20,
            debrisFlow = false, ensembleMembers = 0, ensembleSeed = 42, arrivalDepth = 0.3, warningMinutes = 15, demZoom = 12
        };
        var created = await c.PostAsJsonAsync("/api/runs", spec);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        string status = "";
        for (int i = 0; i < 120 && status is not ("Completed" or "Failed"); i++)
        {
            await Task.Delay(1000);
            var s = await c.GetFromJsonAsync<JsonElement>($"/api/runs/{id}/status");
            status = s.GetProperty("status").GetString()!;
        }
        Assert.Equal("Completed", status);

        // detail contains NaN arrival times somewhere in the engine; it must still serialise
        var detail = await c.GetAsync($"/api/runs/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var doc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("impact", out _));

        var pdf = await c.GetAsync($"/api/runs/{id}/export/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
