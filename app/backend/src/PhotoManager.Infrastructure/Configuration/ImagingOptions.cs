namespace PhotoManager.Infrastructure.Configuration;

/// <summary>Rendition and image-safety settings. Section <c>Imaging</c>.</summary>
public sealed class ImagingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Imaging";

    /// <summary>Gets or sets the longest edge of gallery thumbnails.</summary>
    public int ThumbnailMaxDimension { get; set; } = 480;

    /// <summary>Gets or sets the longest edge of lightbox previews.</summary>
    public int PreviewMaxDimension { get; set; } = 2048;

    /// <summary>
    /// Gets or sets the largest image (in pixels) the worker will decode. Protects against decompression
    /// bombs: a tiny file can declare enormous dimensions and exhaust memory.
    /// </summary>
    public long MaxPixels { get; set; } = 80_000_000;

    /// <summary>Gets or sets the WebP quality for renditions (0-100).</summary>
    public int RenditionQuality { get; set; } = 80;

    /// <summary>
    /// Gets or sets how many images one worker process decodes at the same time. A large PNG or WebP is decoded at
    /// full size first (up to <see cref="MaxPixels"/> × 4 bytes), so this, not the number of activity slots, bounds
    /// the memory decoding needs.
    /// </summary>
    public int MaxConcurrentDecodes { get; set; } = 2;

    /// <summary>
    /// Gets or sets the most memory, in megabytes, ImageSharp may hold at once in one process. Beyond it a decode
    /// fails and is retried later, instead of the container running out of memory.
    /// </summary>
    public int DecodeMemoryLimitMegabytes { get; set; } = 1024;
}
