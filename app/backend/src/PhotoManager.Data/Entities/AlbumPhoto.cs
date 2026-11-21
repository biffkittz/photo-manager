namespace PhotoManager.Data.Entities;

/// <summary>
/// A photo's place in an album. Removing it sets <see cref="RemovedAt"/> instead of deleting the row (neither tier
/// may delete anything); adding it again clears that.
/// </summary>
public sealed class AlbumPhoto
{
    /// <summary>Gets or sets the album.</summary>
    public Guid AlbumId { get; set; }

    /// <summary>Gets or sets the photo.</summary>
    public Guid PhotoId { get; set; }

    /// <summary>Gets or sets when the photo was (last) added.</summary>
    public DateTimeOffset AddedAt { get; set; }

    /// <summary>Gets or sets when the photo was removed from the album; null while it is in it.</summary>
    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>Gets or sets the album (navigation).</summary>
    public Album Album { get; set; } = null!;

    /// <summary>Gets or sets the photo (navigation).</summary>
    public Photo Photo { get; set; } = null!;
}
