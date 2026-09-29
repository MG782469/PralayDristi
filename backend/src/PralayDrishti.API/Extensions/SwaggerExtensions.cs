using Microsoft.OpenApi.Models;

namespace PralayDrishti.API.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddSwaggerWithBearer(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "PralayDrishti API",
                Version = "v1",
                Description = "Dam-break / natural-lake outburst inundation modelling for HADR (SIH 26161). " +
                              "Log in with POST /api/auth/login, then press Authorize and paste the accessToken."
            });

            var scheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT access token (claims: user_id, email, role).",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            };
            c.AddSecurityDefinition("Bearer", scheme);
            c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });

            string xml = Path.Combine(AppContext.BaseDirectory, "PralayDrishti.API.xml");
            if (File.Exists(xml)) c.IncludeXmlComments(xml);
        });
        return services;
    }
}
