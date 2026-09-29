using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Infrastructure.Identity;

namespace PralayDrishti.API.Extensions;

public static class Policies
{
    public const string CanRead = "CanRead";
    public const string CanSimulate = "CanSimulate";
    public const string AdminOnly = "AdminOnly";
}

public static class AuthExtensions
{
    public const string HangfireTokenCookie = "pd_hf";

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            // keep our claim names (user_id / email / role) instead of the long WS-* URIs
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret.PadRight(32))),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = ClaimNames.Email,
                RoleClaimType = ClaimNames.Role
            };
            o.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var path = ctx.HttpContext.Request.Path;
                    // browsers can't set headers on websockets or on the Hangfire dashboard's own requests
                    if (path.StartsWithSegments("/hubs"))
                        ctx.Token ??= ctx.Request.Query["access_token"];
                    else if (path.StartsWithSegments("/hangfire"))
                        ctx.Token ??= ctx.Request.Query["access_token"].FirstOrDefault() ?? ctx.Request.Cookies[HangfireTokenCookie];
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization(o =>
        {
            o.AddPolicy(Policies.CanRead, p => p.RequireAuthenticatedUser());
            o.AddPolicy(Policies.CanSimulate, p => p.RequireRole(nameof(UserRole.Analyst), nameof(UserRole.Admin)));
            o.AddPolicy(Policies.AdminOnly, p => p.RequireRole(nameof(UserRole.Admin)));
        });
        return services;
    }
}
