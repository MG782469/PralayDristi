using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Jobs;
using PralayDrishti.Application.Services;

namespace PralayDrishti.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            // order matters: log everything, reject bad input before touching the cache
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(CachingBehavior<,>));
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddScoped<AuthService>();
        services.AddScoped<RunFactory>();
        services.AddScoped<AlertService>();
        services.AddScoped<SimulationRunService>();
        services.AddScoped<LakeToAlertService>();
        services.AddScoped<AtlasService>();
        services.AddTransient<RunSimulationJob>();
        services.AddTransient<LakeWatchJob>();
        services.AddTransient<RefreshTokenCleanupJob>();
        services.AddTransient<ResultsRetentionJob>();

        return services;
    }
}
