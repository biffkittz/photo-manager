namespace PhotoManager.Infrastructure.Imaging;

/// <summary>Facts about an image established by decoding its header.</summary>
/// <param name="ContentType">The decoded format's MIME type.</param>
/// <param name="Width">Pixel width.</param>
/// <param name="Height">Pixel height.</param>
/// <param name="TakenAt">EXIF capture time, if present and parseable.</param>
public sealed record ImageInspection(string ContentType, int Width, int Height, DateTimeOffset? TakenAt);
