namespace PhotoManager.Api.Contracts;

/// <summary>Requests a download of one photo (original file), several (ZIP), or a whole album (ZIP named after it).</summary>
/// <param name="PhotoIds">Photo ids; leave empty when downloading an album.</param>
/// <param name="AlbumId">The album to download, instead of <paramref name="PhotoIds"/>.</param>
public sealed record CreateDownloadRequest(IReadOnlyList<Guid>? PhotoIds, Guid? AlbumId = null);
