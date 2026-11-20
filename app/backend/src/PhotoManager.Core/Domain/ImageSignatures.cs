namespace PhotoManager.Core.Domain;

/// <summary>
/// Content sniffing for uploads. The browser-supplied filename and Content-Type are never trusted;
/// the type is decided from the first bytes of the file. The worker later re-validates by fully
/// decoding the image.
/// </summary>
public static class ImageSignatures
{
    /// <summary>Number of leading bytes needed by <see cref="Detect"/>.</summary>
    public const int RequiredPrefixLength = 12;

    private static readonly DetectedImageType Jpeg = new("image/jpeg", ".jpg");
    private static readonly DetectedImageType Png = new("image/png", ".png");
    private static readonly DetectedImageType Gif = new("image/gif", ".gif");
    private static readonly DetectedImageType Webp = new("image/webp", ".webp");

    /// <summary>Gets the MIME types the app accepts for upload.</summary>
    public static IReadOnlyList<string> SupportedContentTypes { get; } = ["image/jpeg", "image/png", "image/gif", "image/webp"];

    /// <summary>Detects a supported image type from the start of a file.</summary>
    /// <param name="prefix">At least <see cref="RequiredPrefixLength"/> leading bytes (fewer bytes never match).</param>
    /// <returns>The detected type, or <see langword="null"/> when the content is not a supported image.</returns>
    public static DetectedImageType? Detect(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length >= 3 && prefix[0] == 0xFF && prefix[1] == 0xD8 && prefix[2] == 0xFF)
        {
            return Jpeg;
        }

        if (prefix.Length >= 8 && prefix[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return Png;
        }

        if (prefix.Length >= 6 && (prefix[..6].SequenceEqual("GIF87a"u8) || prefix[..6].SequenceEqual("GIF89a"u8)))
        {
            return Gif;
        }

        if (prefix.Length >= 12 && prefix[..4].SequenceEqual("RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        return null;
    }
}
