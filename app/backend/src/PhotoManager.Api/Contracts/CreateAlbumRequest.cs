namespace PhotoManager.Api.Contracts;

/// <summary>Creates an album, optionally with its first photos.</summary>
/// <param name="Name">Name (1 to 100 characters).</param>
/// <param name="PhotoIds">Photos to put in it (at most the download limit), or none.</param>
public sealed record CreateAlbumRequest(string? Name, IReadOnlyList<Guid>? PhotoIds);
