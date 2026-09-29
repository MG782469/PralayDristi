using Microsoft.EntityFrameworkCore;

namespace PralayDrishti.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DamEntity> Dams => Set<DamEntity>();
    public DbSet<RunEntity> Runs => Set<RunEntity>();
    public DbSet<LakeDetectionEntity> Lakes => Set<LakeDetectionEntity>();
    public DbSet<AlertEntity> Alerts => Set<AlertEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<RunEntity>().Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Entity<RunEntity>().HasIndex(r => r.CreatedUtc);
        b.Entity<AlertEntity>().HasIndex(a => a.CreatedUtc);
        b.Entity<LakeDetectionEntity>().HasIndex(l => l.DetectedUtc);
    }
}
