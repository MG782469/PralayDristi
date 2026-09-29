using Hangfire.Dashboard;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PralayDrishti.API.Extensions;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Infrastructure.Identity;
using PralayDrishti.Infrastructure.Persistence;

namespace PralayDrishti.API.Services;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    private System.Security.Claims.ClaimsPrincipal? User => _http.HttpContext?.User;

    public Guid? UserId => Guid.TryParse(User?.FindFirst(ClaimNames.UserId)?.Value, out var id) ? id : null;
    public string? Email => User?.FindFirst(ClaimNames.Email)?.Value;
    public UserRole? Role => Enum.TryParse<UserRole>(User?.FindFirst(ClaimNames.Role)?.Value, out var r) ? r : null;
    public string? IpAddress => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        // Neon may be waking up - CanConnect doubles as the warm-up query
        await _db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("database unreachable");
}

/// <summary>Hangfire dashboard for Admins only (token arrives via ?access_token= once, then a cookie).</summary>
public sealed class HangfireAdminAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        context.GetHttpContext().User.IsInRole(nameof(UserRole.Admin));
}

/// <summary>Moves ?access_token= into a short-lived HttpOnly cookie so the dashboard's own asset requests authenticate.</summary>
public sealed class HangfireTokenCookieMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _path;

    public HangfireTokenCookieMiddleware(RequestDelegate next, string path)
    {
        _next = next;
        _path = path;
    }

    public Task Invoke(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments(_path) && ctx.Request.Query.TryGetValue("access_token", out var token))
            ctx.Response.Cookies.Append(AuthExtensions.HangfireTokenCookie, token.ToString(), new CookieOptions
            {
                HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = ctx.Request.IsHttps, Path = _path,
                MaxAge = TimeSpan.FromMinutes(15)
            });
        return _next(ctx);
    }
}
