namespace PhotoManager.Core.Domain;

/// <summary>A supported image type detected from file content.</summary>
/// <param name="ContentType">The MIME type.</param>
/// <param name="Extension">The canonical extension including the dot.</param>
public sealed record DetectedImageType(string ContentType, string Extension);
