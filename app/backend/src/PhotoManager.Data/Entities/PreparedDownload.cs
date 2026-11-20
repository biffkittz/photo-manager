using PhotoManager.Core.Domain;

namespace PhotoManager.Data.Entities;

/// <summary>
/// A download of one or more originals. A worker decrypts the originals with the archive key and
/// re-encrypts the result (single file or ZIP) with the web key into private blob storage; the API then
/// streams it to the requester. Plaintext is never written to storage.
/// </summary>
public sealed class PreparedDownload : ITimestamped
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the status.</summary>
    public DownloadStatus Status { get; set; }

    /// <summary>Gets or sets the photos included (at most a configured maximum).</summary>
    public List<Guid> PhotoIds { get; set; } = [];

    /// <summary>Gets or sets the GitHub login of the requester; only they may fetch the content.</summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>Gets or sets the requester's numeric GitHub id; only this user may see or fetch the download.</summary>
    public long RequestedById { get; set; }

    /// <summary>Gets or sets the blob holding the web-key-encrypted content.</summary>
    public string? BlobName { get; set; }

    /// <summary>Gets or sets the filename offered to the browser.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the content type offered to the browser.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Gets or sets the plaintext size.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>Gets or sets how many requested photos were left out because they were no longer available.</summary>
    public int SkippedCount { get; set; }

    /// <summary>Gets or sets a user-safe error message.</summary>
    public string? Error { get; set; }

    /// <summary>Gets or sets when the content becomes unavailable (blob lifecycle deletes it).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets when a worker finished preparing the download. Written by workers only (the web tier's database
    /// role cannot update it); the daily download limit counts these.
    /// </summary>
    public DateTimeOffset? PreparedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token.</summary>
    public uint Version { get; set; }
}
