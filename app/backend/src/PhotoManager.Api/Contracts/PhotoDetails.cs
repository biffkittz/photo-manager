using PhotoManager.Core.Crypto;
using PhotoManager.Core.Domain;

namespace PhotoManager.Api.Contracts;

/// <summary>Full metadata of one photo.</summary>
/// <param name="Id">Photo id.</param>
/// <param name="FileName">The name shown and given to downloads (after the AI description, once there is one).</param>
/// <param name="OriginalFileName">The name the photo arrived with.</param>
/// <param name="Description">Description.</param>
/// <param name="DescriptionSource">Who wrote the description.</param>
/// <param name="DescriptionState">Description availability.</param>
/// <param name="Tags">Tags.</param>
/// <param name="ContentType">MIME type of the original.</param>
/// <param name="SizeBytes">Original size.</param>
/// <param name="Width">Pixel width.</param>
/// <param name="Height">Pixel height.</param>
/// <param name="TakenAt">Capture time.</param>
/// <param name="UploadedAt">Upload time.</param>
/// <param name="UploadedBy">Uploader login.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="StatusDetail">Status explanation.</param>
/// <param name="RenditionState">Rendition availability.</param>
/// <param name="RenditionVersion">Cache-busting version.</param>
/// <param name="EncryptionFormat">Archive format.</param>
/// <param name="Source">Archive index or upload.</param>
public sealed record PhotoDetails(
    Guid Id,
    string FileName,
    string OriginalFileName,
    string? Description,
    DescriptionSource? DescriptionSource,
    ProcessingState DescriptionState,
    IReadOnlyList<string> Tags,
    string ContentType,
    long? SizeBytes,
    int? Width,
    int? Height,
    DateTimeOffset? TakenAt,
    DateTimeOffset UploadedAt,
    string? UploadedBy,
    PhotoStatus Status,
    string? StatusDetail,
    ProcessingState RenditionState,
    int RenditionVersion,
    EncryptionFormat? EncryptionFormat,
    PhotoSource Source);
