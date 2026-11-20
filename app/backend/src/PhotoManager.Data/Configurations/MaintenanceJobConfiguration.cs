using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoManager.Data.Entities;

namespace PhotoManager.Data.Configurations;

/// <summary>Table and indexes for <see cref="MaintenanceJob"/>.</summary>
internal sealed class MaintenanceJobConfiguration : IEntityTypeConfiguration<MaintenanceJob>
{
    public void Configure(EntityTypeBuilder<MaintenanceJob> builder)
    {
        builder.ToTable("maintenance_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.WorkflowId).HasMaxLength(200).IsRequired();
        builder.Property(j => j.RunId).HasMaxLength(64);
        builder.Property(j => j.RequestedBy).HasMaxLength(100).IsRequired();
        builder.Property(j => j.Summary).HasMaxLength(1000);
        builder.Property(j => j.Version).IsRowVersion();
        builder.HasIndex(j => j.CreatedAt).IsDescending();
    }
}
