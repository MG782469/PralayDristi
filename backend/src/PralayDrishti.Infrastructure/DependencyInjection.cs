using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Domain.Interfaces;
using PralayDrishti.Infrastructure.BackgroundJobs;
using PralayDrishti.Infrastructure.Identity;
using PralayDrishti.Infrastructure.Persistence;
using PralayDrishti.Infrastructure.Persistence.Repositories;
using PralayDrishti.Infrastructure.Persistence.Seed;
using PralayDrishti.Infrastructure.Services;

namespace PralayDrishti.Infrastructure;

public static partial class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.AddPersistence(config, env);
        services.AddIdentityServices(config);
        services.AddCachingAndJobs(config);
        services.AddEngine(config, env);
        return services;
    }

    public static IServiceCollection AddEngine(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var opt = config.GetSection("PralayDrishti").Get<AppOptions>() ?? new AppOptions();
        string dataDir = Path.IsPathRooted(opt.DataDir) ? opt.DataDir : Path.Combine(env.ContentRootPath, opt.DataDir);
        services.AddSingleton(opt);
        services.AddSingleton(new DataPaths(dataDir));

        services.AddHttpClient("tiles", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("PralayDrishti/1.0");
        });
        services.AddHttpClient("overpass", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(120);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("PralayDrishti/1.0 (dam-break decision support)");
        });

        services.AddSingleton<DemProvider>();
        services.AddSingleton<IDemProvider>(sp => sp.GetRequiredService<DemProvider>());
        services.AddSingleton<ExposureProvider>();
        services.AddSingleton<IExposureProvider>(sp => sp.GetRequiredService<ExposureProvider>());
        services.AddSingleton<ResultStore>();
        services.AddSingleton<IResultStore>(sp => sp.GetRequiredService<ResultStore>());
        services.AddSingleton<IFloodSimulationEngine, FloodSimulationEngine>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddScoped<IMapRenderer, MapRenderer>();
        services.AddSingleton<IHydraulicAnalysis, HydraulicAnalysis>();
        services.AddSingleton<IValidationBenchmarks, ValidationBenchmarks>();
        services.AddSingleton<IFileStorage, FileStorage>();
        services.AddSingleton<IEnvironmentInfo, EnvironmentInfo>();
        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        string? cs = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is empty. Put your Neon connection string in appsettings.json " +
                "(or run docker-compose.dev.yml and use appsettings.Development.json).");

        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(cs, npg => npg.EnableRetryOnFailure(5).CommandTimeout(60))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IDamRepository, DamRepository>();
        services.AddScoped<ISimulationRunRepository, SimulationRunRepository>();
        services.AddScoped<ILakeDetectionRepository, LakeDetectionRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.Configure<SeedOptions>(config.GetSection("Seed"));
        return services;
    }

    public static IServiceCollection AddIdentityServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section))
            .Validate(o => System.Text.Encoding.UTF8.GetByteCount(o.Secret) >= 32, "Jwt:Secret must be at least 32 bytes")
            .ValidateOnStart();
        services.AddSingleton<IDateTimeProvider, SystemClock>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        return services;
    }
}
