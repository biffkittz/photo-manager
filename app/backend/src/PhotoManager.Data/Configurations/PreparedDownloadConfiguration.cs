using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoManager.Data.Entities;

namespace PhotoManager.Data.Configurations;

/// <summary>Table and indexes for <see cref="PreparedDownload"/>.</summary>
internal sealed class PreparedDownloadConfiguration : IEntityTypeConfiguration<PreparedDownload>
{
    public void Configure(EntityTypeBuilder<PreparedDownload> builder)
    {
        builder.ToTable("prepared_downloads");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.RequestedBy).HasMaxLength(100).IsRequired();
        builder.Property(d => d.BlobName).HasMaxLength(200);
        builder.Property(d => d.FileName).HasMaxLength(255).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Error).HasMaxLength(500);
        builder.Property(d => d.Version).IsRowVersion();
        builder.HasIndex(d => new { d.RequestedBy, d.CreatedAt }).IsDescending(false, true);
    }
}
