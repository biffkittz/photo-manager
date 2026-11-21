using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoManager.Data.Entities;

namespace PhotoManager.Data.Configurations;

/// <summary>Table and keys for <see cref="AlbumPhoto"/>.</summary>
internal sealed class AlbumPhotoConfiguration : IEntityTypeConfiguration<AlbumPhoto>
{
    public void Configure(EntityTypeBuilder<AlbumPhoto> builder)
    {
        builder.ToTable("album_photos");
        builder.HasKey(ap => new { ap.AlbumId, ap.PhotoId });

        // Rows are never deleted (removing a photo from an album sets removed_at), so nothing cascades.
        builder.HasOne(ap => ap.Album).WithMany(a => a.Photos).HasForeignKey(ap => ap.AlbumId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(ap => ap.Photo).WithMany().HasForeignKey(ap => ap.PhotoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(ap => ap.PhotoId);
    }
}
