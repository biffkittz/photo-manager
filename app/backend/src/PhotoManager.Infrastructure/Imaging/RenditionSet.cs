namespace PhotoManager.Infrastructure.Imaging;

/// <summary>Encoded WebP renditions (plaintext; encrypted with the web key before storage).</summary>
/// <param name="Thumbnail">Small rendition for the gallery grid.</param>
/// <param name="Preview">Large rendition for the lightbox.</param>
public sealed record RenditionSet(byte[] Thumbnail, byte[] Preview);
