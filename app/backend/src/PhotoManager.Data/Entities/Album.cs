namespace PhotoManager.Data.Entities;

/// <summary>
/// A named group of photos (a trip, a holiday, "the kids"). Albums are shared by the whole family: everyone who can
/// sign in sees every album and can change it. Nothing is ever deleted: deleting an album sets
/// <see cref="DeletedAt"/>, and its photos stay in the library.
/// </summary>
public sealed class Album : ITimestamped
{
    /// <summary>Maximum length of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Gets or sets the identifier (UUIDv7).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the name shown in the app (and used for the album's ZIP download).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the photo chosen as the cover. It only counts while that photo is in the album; otherwise (and
    /// when none was chosen) the album's first photo is its cover.
    /// </summary>
    public Guid? CoverPhotoId { get; set; }

    /// <summary>Gets or sets the user id of whoever created the album (see <c>Photo.UploadedById</c>).</summary>
    public long CreatedById { get; set; }

    /// <summary>Gets or sets when the album was deleted; null while it exists.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the album last changed (renamed, photos added or removed); the albums page lists the latest first.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token.</summary>
    public uint Version { get; set; }

    /// <summary>Gets or sets the album's photos, including removed ones (<see cref="AlbumPhoto.RemovedAt"/>).</summary>
    public List<AlbumPhoto> Photos { get; set; } = [];
}
