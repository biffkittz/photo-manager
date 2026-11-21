namespace PhotoManager.Api.Contracts;

/// <summary>Photos to add to or remove from an album.</summary>
/// <param name="PhotoIds">Photo ids (1 up to the download limit).</param>
public sealed record AlbumPhotosRequest(IReadOnlyList<Guid>? PhotoIds);
