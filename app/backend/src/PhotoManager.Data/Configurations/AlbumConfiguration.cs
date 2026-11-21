using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoManager.Data.Entities;

namespace PhotoManager.Data.Configurations;

/// <summary>Table and keys for <see cref="Album"/>.</summary>
internal sealed class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.ToTable("albums");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Name).HasMaxLength(Album.MaxNameLength).IsRequired();
        builder.Property(a => a.Version).IsRowVersion();

        // Photos are never deleted; should one ever be removed by hand, the album falls back to its first photo.
        builder.HasOne<Photo>().WithMany().HasForeignKey(a => a.CoverPhotoId).OnDelete(DeleteBehavior.SetNull);
    }
}
