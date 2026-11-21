namespace PhotoManager.Api.Contracts;

/// <summary>An album as listed on the albums page and shown at the top of its own page.</summary>
/// <param name="Id">Album id.</param>
/// <param name="Name">Name.</param>
/// <param name="PhotoCount">Photos in the album (library photos only).</param>
/// <param name="FirstDate">Capture date (else upload date) of its oldest photo, if any.</param>
/// <param name="LastDate">Capture date (else upload date) of its newest photo, if any.</param>
/// <param name="Cover">The chosen cover photo while it is in the album, else the first photo; null when empty.</param>
public sealed record AlbumSummary(Guid Id, string Name, int PhotoCount, DateTimeOffset? FirstDate, DateTimeOffset? LastDate, PhotoSummary? Cover);
