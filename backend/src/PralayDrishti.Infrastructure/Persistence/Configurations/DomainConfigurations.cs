using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PralayDrishti.Domain.Entities;

namespace PralayDrishti.Infrastructure.Persistence.Configurations;

public sealed class DamConfiguration : IEntityTypeConfiguration<Dam>
{
    public void Configure(EntityTypeBuilder<Dam> b)
    {
        b.ToTable("dams");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityByDefaultColumn();
        b.Property(x => x.Name).IsRequired().HasMaxLength(160);
        b.Property(x => x.River).IsRequired().HasMaxLength(80);
        b.Property(x => x.State).IsRequired().HasMaxLength(80);
        b.Property(x => x.Type).IsRequired().HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Category).IsRequired().HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Notes).IsRequired().HasMaxLength(400);
        b.Property(x => x.Source).IsRequired().HasMaxLength(200);
        b.Property(x => x.Lat).IsRequired();
        b.Property(x => x.Lon).IsRequired();
        b.Property(x => x.HeightM).IsRequired();
        b.Property(x => x.GrossStorageMcm).IsRequired();
        b.Property(x => x.CascadeWith).IsRequired().AsJsonb();
        b.Property(x => x.Dykes).AsJsonb();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

public sealed class SimulationRunConfiguration : IEntityTypeConfiguration<SimulationRun>
{
    public void Configure(EntityTypeBuilder<SimulationRun> b)
    {
        b.ToTable("simulation_runs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).IsRequired().HasMaxLength(160);
        b.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Origin).IsRequired().HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Mode).IsRequired().HasMaxLength(30);
        b.Property(x => x.Stage).IsRequired().HasMaxLength(120);
        b.Property(x => x.SpecJson).IsRequired().HasColumnType("jsonb");
        b.Property(x => x.SummaryJson).HasColumnType("jsonb");
        b.Property(x => x.Error).HasColumnType("text");
        b.Property(x => x.JobId).HasMaxLength(64);
        b.Property(x => x.CreatedUtc).IsRequired();
        b.Ignore(x => x.IsFinished);

        b.HasOne(x => x.Dam).WithMany().HasForeignKey(x => x.DamId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => x.CreatedUtc);
        b.HasIndex(x => new { x.Origin, x.Status });
    }
}

public sealed class LakeDetectionConfiguration : IEntityTypeConfiguration<LakeDetection>
{
    public void Configure(EntityTypeBuilder<LakeDetection> b)
    {
        b.ToTable("lake_detections");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.DetectedUtc).IsRequired();
        b.Property(x => x.Source).IsRequired().HasMaxLength(60);
        b.Property(x => x.Region).IsRequired().HasMaxLength(80);
        b.Property(x => x.State).IsRequired().HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Notes).HasColumnType("text");
        b.Property(x => x.Polygon).AsJsonb();
        b.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => x.DetectedUtc);
    }
}

public sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> b)
    {
        b.ToTable("alerts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Headline).IsRequired().HasMaxLength(200);
        b.Property(x => x.Severity).IsRequired().HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CapXml).IsRequired().HasColumnType("text");
        b.Property(x => x.CreatedUtc).IsRequired();
        b.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.CreatedUtc);
    }
}
