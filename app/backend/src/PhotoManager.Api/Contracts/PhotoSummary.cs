using PhotoManager.Core.Domain;

namespace PhotoManager.Api.Contracts;

/// <summary>A gallery tile.</summary>
/// <param name="Id">Photo id.</param>
/// <param name="FileName">Original filename.</param>
/// <param name="Description">Description, if any.</param>
/// <param name="UploadedAt">Upload time.</param>
/// <param name="TakenAt">Capture time, if known.</param>
/// <param name="Width">Pixel width, if known.</param>
/// <param name="Height">Pixel height, if known.</param>
/// <param name="RenditionState">Thumbnail availability.</param>
/// <param name="RenditionVersion">Cache-busting version for rendition URLs.</param>
public sealed record PhotoSummary(
    Guid Id,
    string FileName,
    string? Description,
    DateTimeOffset UploadedAt,
    DateTimeOffset? TakenAt,
    int? Width,
    int? Height,
    ProcessingState RenditionState,
    int RenditionVersion);
