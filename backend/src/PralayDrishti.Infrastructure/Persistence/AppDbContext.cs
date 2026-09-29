using Microsoft.EntityFrameworkCore;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Infrastructure.Persistence;

public class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Dam> Dams => Set<Dam>();
    public DbSet<SimulationRun> Runs => Set<SimulationRun>();
    public DbSet<LakeDetection> Lakes => Set<LakeDetection>();
    public DbSet<Alert> Alerts => Set<Alert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // every DateTime is UTC and stored as timestamptz; enums are readable strings in the database
        b.Properties<DateTime>().HaveColumnType("timestamp with time zone");
        b.Properties<DateTime?>().HaveColumnType("timestamp with time zone");
    }

    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);
}
