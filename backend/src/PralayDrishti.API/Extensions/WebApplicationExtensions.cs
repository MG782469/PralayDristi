using Microsoft.AspNetCore.HttpOverrides;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PralayDrishti.API.Hubs;
using PralayDrishti.API.Middleware;
using PralayDrishti.API.Services;
using PralayDrishti.Application.Common.Options;
using PralayDrishti.Infrastructure.BackgroundJobs;
using PralayDrishti.Infrastructure.Persistence;

namespace PralayDrishti.API.Extensions;

public static class WebApplicationExtensions
{
    public static async Task UsePresentationAsync(this WebApplication app)
    {
        await app.Services.InitializeDatabaseAsync(app.Configuration);

        // behind a load balancer / ingress: real client IP (stored on refresh tokens) and scheme
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseSwagger();
        app.UseSwaggerUI(c => c.DocumentTitle = "PralayDrishti API");
        app.UseCors();
        // the Docker image copies the built dashboard into wwwroot, so one container serves everything
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Services.GetRequiredService<RedisStatus>().Connected)
        {
            var path = app.Configuration.GetSection(HangfireJobOptions.Section).Get<HangfireJobOptions>()?.DashboardPath ?? "/hangfire";
            app.UseMiddleware<HangfireTokenCookieMiddleware>(path);
            app.UseHangfireDashboard(path, new DashboardOptions
            {
                Authorization = new[] { new HangfireAdminAuthorizationFilter() },
                DashboardTitle = "PralayDrishti jobs"
            });
        }

        app.MapControllers();
        app.MapHub<RunHub>("/hubs/runs");
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
        app.MapFallbackToFile("index.html");
    }
}
