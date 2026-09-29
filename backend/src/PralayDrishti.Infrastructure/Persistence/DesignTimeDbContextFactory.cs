using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PralayDrishti.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` so migrations can be generated without starting the API.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        string cs = Environment.GetEnvironmentVariable("PD_DESIGN_CONNECTION")
                    ?? "Host=localhost;Port=5432;Database=pralaydrishti;Username=pralay;Password=pralay";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs)
            .UseSnakeCaseNamingConvention()
            .Options);
    }
}
