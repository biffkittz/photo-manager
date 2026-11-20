using System.Text.RegularExpressions;
using NpgsqlTypes;
using PhotoManager.Core.Crypto;
using PhotoManager.Core.Domain;

namespace PhotoManager.Data.Entities;

/// <summary>
/// Metadata for one photo. The image bytes never live in the database: originals are in the Wasabi
/// archive (<see cref="ArchiveKey"/>), renditions are in private Azure Blob storage.
/// </summary>
public sealed partial class Photo : ITimestamped
{
    /// <summary>Maximum length of <see cref="SearchKeywords"/>.</summary>
    public const int MaxSearchKeywordsLength = 1000;

    /// <summary>Gets or sets the identifier (UUIDv7: time-ordered, so inserts stay index-friendly).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets where the record came from.</summary>
    public PhotoSource Source { get; set; }

    /// <summary>Gets or sets the lifecycle status.</summary>
    public PhotoStatus Status { get; set; }

    /// <summary>Gets or sets a short, user-safe explanation for <see cref="PhotoStatus.Failed"/> and similar states.</summary>
    public string? StatusDetail { get; set; }

    /// <summary>Gets or sets the archive encryption format (null until archived).</summary>
    public EncryptionFormat? EncryptionFormat { get; set; }

    /// <summary>Gets or sets the Wasabi object key of the encrypted original (unique).</summary>
    public string? ArchiveKey { get; set; }

    /// <summary>Gets or sets the stored (ciphertext) size.</summary>
    public long? ArchiveSizeBytes { get; set; }

    /// <summary>Gets or sets the object ETag, used by re-indexing to detect replaced objects.</summary>
    public string? ArchiveETag { get; set; }

    /// <summary>Gets or sets the sanitized original filename.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the MIME type of the original.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Gets or sets the plaintext size, when known.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>Gets or sets the lowercase hex SHA-256 of the plaintext (duplicate detection).</summary>
    public string? Sha256 { get; set; }

    /// <summary>Gets or sets the description (AI-generated or user-edited).</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets who produced <see cref="Description"/>.</summary>
    public DescriptionSource? DescriptionSource { get; set; }

    /// <summary>Gets or sets the Gemini model that produced the description, for provenance.</summary>
    public string? DescriptionModel { get; set; }

    /// <summary>Gets or sets AI-generated tags.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets extra full-text search terms: the tags plus the words of the filename. Legacy filenames
    /// are often descriptive ("woman_and_boy_smiling_on_beach.jpg", written by protect.ps1's rename feature),
    /// but the PostgreSQL parser treats a whole filename as one token, so its words are split out here.
    /// Maintained by <see cref="RefreshSearchKeywords"/>, which <see cref="PhotoDbContext"/> calls on save.
    /// </summary>
    public string SearchKeywords { get; set; } = string.Empty;

    /// <summary>Gets or sets the state of the AI description.</summary>
    public ProcessingState DescriptionState { get; set; }

    /// <summary>Gets or sets the state of the thumbnail/preview renditions.</summary>
    public ProcessingState RenditionState { get; set; }

    /// <summary>Gets or sets a counter bumped whenever renditions are regenerated; used to cache-bust URLs.</summary>
    public int RenditionVersion { get; set; }

    /// <summary>Gets or sets the pixel width of the original.</summary>
    public int? Width { get; set; }

    /// <summary>Gets or sets the pixel height of the original.</summary>
    public int? Height { get; set; }

    /// <summary>Gets or sets when the photo was taken (EXIF DateTimeOriginal), if known.</summary>
    public DateTimeOffset? TakenAt { get; set; }

    /// <summary>
    /// Gets or sets the capture date used for sorting: <see cref="TakenAt"/>, or <see cref="UploadedAt"/> when unknown.
    /// Computed by the database (stored generated column) so the gallery's "taken" order can use an index.
    /// </summary>
    public DateTimeOffset SortDate { get; set; }

    /// <summary>Gets or sets when the photo was uploaded (legacy: the date folder of the object key).</summary>
    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>Gets or sets the GitHub login of the uploader.</summary>
    public string? UploadedBy { get; set; }

    /// <summary>
    /// Gets or sets the uploader's numeric GitHub id. Ownership checks use this immutable id; <see cref="UploadedBy"/>
    /// (the login, which can be renamed or re-registered by someone else) is for display only.
    /// </summary>
    public long? UploadedById { get; set; }

    /// <summary>Gets or sets the client-generated batch identifier grouping one upload session.</summary>
    public Guid? UploadBatchId { get; set; }

    /// <summary>
    /// Gets or sets when the archive object was last confirmed to exist (by a re-index scan or by ingest).
    /// Re-indexing marks photos not seen since its start as <see cref="PhotoStatus.Missing"/>; timestamps
    /// (rather than a scan id) keep overlapping scans from interfering with each other.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets the generated full-text search vector (description, filename, tags).</summary>
    public NpgsqlTsVector SearchVector { get; set; } = null!;

    /// <summary>Gets or sets the optimistic concurrency token (maps to PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }

    /// <summary>Replaces the tags and refreshes <see cref="SearchKeywords"/>.</summary>
    /// <param name="tags">The new tags.</param>
    public void SetTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        Tags = [.. tags];
        RefreshSearchKeywords();
    }

    /// <summary>Recomputes <see cref="SearchKeywords"/> from the tags and filename.</summary>
    public void RefreshSearchKeywords() => SearchKeywords = BuildSearchKeywords(Tags, OriginalFileName);

    /// <summary>Builds the search keywords for the given tags and filename.</summary>
    /// <param name="tags">Tags.</param>
    /// <param name="fileName">Original filename.</param>
    /// <returns>Space-separated keywords, length-capped.</returns>
    public static string BuildSearchKeywords(IEnumerable<string> tags, string? fileName)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var words = NonWordCharacters().Split(Path.GetFileNameWithoutExtension(fileName ?? string.Empty))
            .Where(w => w.Length > 1);
        var keywords = string.Join(' ', tags.Concat(words));
        return keywords.Length <= MaxSearchKeywordsLength ? keywords : keywords[..MaxSearchKeywordsLength];
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWordCharacters();
}
