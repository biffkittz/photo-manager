using System.Text.Json.Serialization;

namespace PhotoManager.Core.Crypto;

/// <summary>
/// Metadata sealed inside v2 archive files (encrypted and authenticated with the content). It makes the
/// bucket self-describing: if the database is lost, a re-index can rebuild filenames, descriptions and
/// hashes from the archive alone. It is a snapshot taken at upload time; later edits (for example a
/// user-edited description) live in the database, which remains the source of truth.
/// </summary>
public sealed record EmbeddedPhotoMetadata
{
    /// <summary>Gets the schema version of this record. Bump when adding required fields.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Gets the photo identifier (also part of the object key).</summary>
    public required Guid PhotoId { get; init; }

    /// <summary>Gets the original filename supplied by the uploader (sanitized).</summary>
    public required string OriginalFileName { get; init; }

    /// <summary>Gets the detected MIME type of the original.</summary>
    public required string ContentType { get; init; }

    /// <summary>Gets the plaintext size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Gets the lowercase hex SHA-256 of the plaintext.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Gets the AI-generated description at upload time, if one was produced.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the AI-generated tags at upload time.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets when the photo was taken (EXIF), if known.</summary>
    public DateTimeOffset? TakenAt { get; init; }

    /// <summary>Gets when the photo was uploaded.</summary>
    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>Gets the GitHub login of the uploader.</summary>
    public string? UploadedBy { get; init; }
}

/// <summary>Source-generated JSON serialization for core types (trimming/AOT friendly, no reflection).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EmbeddedPhotoMetadata))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
