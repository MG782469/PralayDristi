using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using PralayDrishti.API.Hubs;
using PralayDrishti.API.Services;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Options;
using PralayDrishti.Infrastructure.BackgroundJobs;

namespace PralayDrishti.API.Extensions;

public static class DependencyInjection
{
    public const long MaxUploadBytes = 4_000_000_000;

    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration config)
    {
        services.AddControllers().AddJsonOptions(o =>
        {
            // engine payloads contain NaN (never-flooded arrival times) and tuple legends
            o.JsonSerializerOptions.IncludeFields = true;
            o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
            o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        services.AddProblemDetails();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services.AddSignalR();
        services.AddSingleton<IRunNotifier, SignalRRunNotifier>();

        services.AddOptions<AuthOptions>().Bind(config.GetSection(AuthOptions.Section));
        services.AddOptions<LakeWatchOptions>().Bind(config.GetSection(LakeWatchOptions.Section));
        services.AddOptions<HangfireJobOptions>().Bind(config.GetSection(HangfireJobOptions.Section));

        services.AddJwtAuthentication(config);
        services.AddSwaggerWithBearer();
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" })
            .AddCheck<RedisHealthCheck>("redis", tags: new[] { "ready" });

        string[] origins = config.GetSection("PralayDrishti:CorsOrigins").Get<string[]>() ?? Array.Empty<string>();
        services.AddCors(c => c.AddDefaultPolicy(p =>
        {
            // credentials (refresh cookie) need explicit origins
            if (origins.Length > 0) p.WithOrigins(origins).AllowCredentials();
            else p.AllowAnyOrigin();
            p.AllowAnyHeader().AllowAnyMethod();
        }));

        services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxUploadBytes);
        services.Configure<KestrelServerOptions>(k => k.Limits.MaxRequestBodySize = MaxUploadBytes);
        return services;
    }
}
