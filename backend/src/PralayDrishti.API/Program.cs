using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using PralayDrishti.API.Data;
using PralayDrishti.API.Hubs;
using PralayDrishti.API.Services;

var builder = WebApplication.CreateBuilder(args);

var opt = builder.Configuration.GetSection("PralayDrishti").Get<AppOptions>() ?? new AppOptions();
var dataDir = Path.IsPathRooted(opt.DataDir) ? opt.DataDir : Path.Combine(builder.Environment.ContentRootPath, opt.DataDir);
var paths = new DataPaths(dataDir);
builder.Services.AddSingleton(opt);
builder.Services.AddSingleton(paths);

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={paths.Database}"));

builder.Services.AddHttpClient("tiles", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("PralayDrishti/1.0");
});
builder.Services.AddHttpClient("overpass", c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("PralayDrishti/1.0 (dam-break decision support)");
});

builder.Services.AddSingleton<DemProvider>();
builder.Services.AddSingleton<ExposureProvider>();
builder.Services.AddSingleton<RunStore>();
builder.Services.AddSingleton<RunQueue>();
builder.Services.AddSingleton<LakeToAlertChain>();
builder.Services.AddSingleton<AtlasService>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddHostedService<SimulationWorker>();
builder.Services.AddHostedService<LakeWatchService>();

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.IncludeFields = true;
    o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() { Title = "PralayDrishti API", Version = "v1",
    Description = "Dam-break / natural-lake outburst inundation modelling for HADR (SIH 26161)" }));
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 4_000_000_000);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 4_000_000_000);

builder.Services.AddCors(c => c.AddDefaultPolicy(p =>
{
    if (opt.CorsOrigins.Length > 0) p.WithOrigins(opt.CorsOrigins).AllowCredentials();
    else p.AllowAnyOrigin();
    p.AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db, paths, app.Logger);
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
// the Docker image copies the built dashboard into wwwroot, so one container serves everything
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapHub<RunHub>("/hubs/runs");
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));
app.MapFallbackToFile("index.html");

app.Logger.LogInformation("PralayDrishti data folder: {Dir}", paths.Root);
app.Run();
