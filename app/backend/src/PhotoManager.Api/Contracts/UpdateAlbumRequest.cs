namespace PhotoManager.Api.Contracts;

/// <summary>Renames an album and/or chooses its cover; omitted values stay as they are.</summary>
/// <param name="Name">New name (1 to 100 characters).</param>
/// <param name="CoverPhotoId">A photo in the album to show as its cover.</param>
public sealed record UpdateAlbumRequest(string? Name, Guid? CoverPhotoId);
