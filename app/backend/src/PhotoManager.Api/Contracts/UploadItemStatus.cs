using PhotoManager.Core.Domain;

namespace PhotoManager.Api.Contracts;

/// <summary>Progress of one uploaded photo through ingestion.</summary>
/// <param name="PhotoId">Photo id.</param>
/// <param name="FileName">The name the photo was uploaded with.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="StatusDetail">Explanation for failures/duplicates.</param>
/// <param name="Description">Description once generated.</param>
/// <param name="DescriptionState">Description availability.</param>
/// <param name="RenditionState">Thumbnail availability.</param>
/// <param name="RenditionVersion">Cache-busting version.</param>
/// <param name="UploadedAt">Upload time.</param>
public sealed record UploadItemStatus(
    Guid PhotoId,
    string FileName,
    PhotoStatus Status,
    string? StatusDetail,
    string? Description,
    ProcessingState DescriptionState,
    ProcessingState RenditionState,
    int RenditionVersion,
    DateTimeOffset UploadedAt);
