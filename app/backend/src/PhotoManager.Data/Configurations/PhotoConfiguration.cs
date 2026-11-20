using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoManager.Core.Domain;
using PhotoManager.Data.Entities;

namespace PhotoManager.Data.Configurations;

/// <summary>Table, constraints and indexes for <see cref="Photo"/>.</summary>
internal sealed class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.ToTable("photos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.StatusDetail).HasMaxLength(500);
        builder.Property(p => p.ArchiveKey).HasMaxLength(1024);
        builder.Property(p => p.ArchiveETag).HasMaxLength(128);
        builder.Property(p => p.OriginalFileName).HasMaxLength(FileNames.MaxLength).IsRequired();
        builder.Property(p => p.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Sha256).HasMaxLength(64).IsFixedLength();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.DescriptionModel).HasMaxLength(100);
        builder.Property(p => p.SearchKeywords).HasMaxLength(Photo.MaxSearchKeywordsLength).IsRequired();
        builder.Property(p => p.UploadedBy).HasMaxLength(100);
        builder.Property(p => p.SortDate).HasComputedColumnSql("COALESCE(taken_at, uploaded_at)", stored: true);
        builder.Property(p => p.Version).IsRowVersion();

        // Full-text search over description, filename and tags ("smiling" matches "smile").
        builder.HasGeneratedTsVectorColumn(
                p => p.SearchVector,
                "english",
                p => new { p.Description, p.OriginalFileName, p.SearchKeywords })
            .HasIndex(p => p.SearchVector)
            .HasMethod("GIN");

        builder.HasIndex(p => p.ArchiveKey).IsUnique().HasFilter("archive_key IS NOT NULL");
        builder.HasIndex(p => new { p.Status, p.UploadedAt, p.Id }).IsDescending(false, true, true);
        builder.HasIndex(p => new { p.Status, p.SortDate, p.Id }).IsDescending(false, true, true);
        builder.HasIndex(p => p.Sha256);

        // At most one ready photo per content: byte-identical copies (uploaded twice, or present twice in the
        // archive) are kept as Duplicate rows and hidden. Enforced here so concurrent ingests cannot race past it.
        builder.HasIndex(p => p.Sha256, DbErrors.UniqueReadyContentIndex)
            .HasDatabaseName(DbErrors.UniqueReadyContentIndex)
            .IsUnique()
            .HasFilter("status = 'Ready' AND sha256 IS NOT NULL");
        builder.HasIndex(p => new { p.UploadedById, p.UploadedAt });
        builder.HasIndex(p => p.UploadBatchId);
        builder.HasIndex(p => new { p.RenditionState, p.Id });
        builder.HasIndex(p => new { p.DescriptionState, p.Id });
        builder.HasIndex(p => new { p.Status, p.LastSeenAt });
    }
}
