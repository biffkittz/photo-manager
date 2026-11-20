using System.Globalization;
using PhotoManager.Core.Crypto;

namespace PhotoManager.Core.Domain;

/// <summary>
/// Object-key layout of the Wasabi archive bucket.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Legacy</b> (written by <c>protect.ps1</c>): <c>yyyy-MM-dd/&lt;name&gt;.encrypted</c>, where the date is the
/// upload day and the name is the original filename with '-' replaced by '_'.</item>
/// <item><b>v2</b> (written by this app): <c>v2/yyyy/MM/&lt;photo id&gt;.pmv2</c>. Keys deliberately contain no
/// filename or description; those are sealed inside the encrypted file (<see cref="EmbeddedPhotoMetadata"/>),
/// so the storage provider learns nothing about the content.</item>
/// </list>
/// The distinct extension stops <c>protect.ps1</c> from trying (and failing) to decrypt v2 files.
/// </remarks>
public static class ArchiveObjectKeys
{
    /// <summary>Prefix of every v2 object.</summary>
    public const string V2Prefix = "v2/";

    /// <summary>Extension of v2 archive objects.</summary>
    public const string V2Extension = ".pmv2";

    /// <summary>Extension of legacy <c>protect.ps1</c> objects.</summary>
    public const string LegacyExtension = ".encrypted";

    /// <summary>Builds the deterministic key for a v2 original. Deterministic keys make archive uploads idempotent.</summary>
    /// <param name="photoId">The photo identifier.</param>
    /// <param name="uploadedAt">Upload timestamp (UTC month folders).</param>
    /// <returns>The object key.</returns>
    public static string ForV2(Guid photoId, DateTimeOffset uploadedAt)
    {
        var utc = uploadedAt.ToUniversalTime();
        return string.Create(CultureInfo.InvariantCulture, $"{V2Prefix}{utc:yyyy}/{utc:MM}/{photoId:N}{V2Extension}");
    }

    /// <summary>Classifies a bucket key. Non-photo keys (folders, other files) return <see langword="false"/>.</summary>
    /// <param name="key">The object key.</param>
    /// <param name="format">The detected format.</param>
    /// <returns>Whether the key is a photo the app manages.</returns>
    public static bool TryClassify(string key, out EncryptionFormat format)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.StartsWith(V2Prefix, StringComparison.Ordinal) && key.EndsWith(V2Extension, StringComparison.Ordinal))
        {
            // Only names this app writes (v2/yyyy/MM/<photo id>.pmv2) are photos; other objects under v2/ are ignored
            // rather than misread as legacy files.
            format = EncryptionFormat.V2;
            return TryParseV2Name(key, out _);
        }

        if (key.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase) && key.Length > LegacyExtension.Length && !key.EndsWith('/'))
        {
            format = EncryptionFormat.LegacyV1;
            return true;
        }

        format = default;
        return false;
    }

    /// <summary>Extracts the photo id from a v2 key.</summary>
    /// <param name="key">The object key.</param>
    /// <param name="photoId">The parsed id.</param>
    /// <returns>Whether the key is a well-formed v2 key.</returns>
    public static bool TryParseV2PhotoId(string key, out Guid photoId)
    {
        photoId = Guid.Empty;
        if (!TryClassify(key, out var format) || format != EncryptionFormat.V2)
        {
            return false;
        }

        return TryParseV2Name(key, out photoId);
    }

    /// <summary>
    /// Whether <paramref name="key"/> is an archive object that can hold this photo: a v2 key must carry the photo's
    /// own id, a legacy key must name a legacy archive file. Workers check this before decrypting anything, so a
    /// database row pointed at some other object in the bucket is never decrypted for it.
    /// </summary>
    /// <param name="key">The object key recorded for the photo.</param>
    /// <param name="photoId">The photo's id.</param>
    /// <param name="format">The encryption format recorded for the photo.</param>
    /// <returns>Whether the key is consistent with the photo.</returns>
    public static bool BelongsTo(string key, Guid photoId, EncryptionFormat format)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!TryClassify(key, out var keyFormat) || keyFormat != format)
        {
            return false;
        }

        return format != EncryptionFormat.V2 || (TryParseV2Name(key, out var keyPhotoId) && keyPhotoId == photoId);
    }

    private static bool TryParseV2Name(string key, out Guid photoId)
    {
        var fileName = key[(key.LastIndexOf('/') + 1)..^V2Extension.Length];
        return Guid.TryParseExact(fileName, "N", out photoId);
    }

    /// <summary>Derives the original filename from a legacy key (last segment without <c>.encrypted</c>).</summary>
    /// <param name="key">The legacy object key.</param>
    /// <returns>The filename, for example <c>IMG_1234.jpg</c>.</returns>
    public static string LegacyFileName(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var lastSegment = key[(key.LastIndexOf('/') + 1)..];
        return lastSegment.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase)
            ? lastSegment[..^LegacyExtension.Length]
            : lastSegment;
    }

    /// <summary>Parses the <c>yyyy-MM-dd/</c> upload-date prefix that <c>protect.ps1</c> writes.</summary>
    /// <param name="key">The legacy object key.</param>
    /// <returns>The date at midnight UTC, or <see langword="null"/> when the key has no such prefix.</returns>
    public static DateTimeOffset? LegacyUploadDate(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var slash = key.IndexOf('/', StringComparison.Ordinal);
        if (slash != 10)
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(
            key.AsSpan(0, 10),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var date)
            ? date
            : null;
    }

    /// <summary>Guesses a MIME type from a filename extension (legacy objects carry no content type).</summary>
    /// <param name="fileName">The filename.</param>
    /// <returns>The MIME type, or <c>application/octet-stream</c>.</returns>
    public static string GuessContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToUpperInvariant();
        return extension switch
        {
            ".JPG" or ".JPEG" => "image/jpeg",
            ".PNG" => "image/png",
            ".WEBP" => "image/webp",
            ".GIF" => "image/gif",
            _ => "application/octet-stream",
        };
    }
}
