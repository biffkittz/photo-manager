namespace PhotoManager.Api.Contracts;

/// <summary>Requests a download of one photo (original file) or several (ZIP).</summary>
/// <param name="PhotoIds">Photo ids.</param>
public sealed record CreateDownloadRequest(IReadOnlyList<Guid> PhotoIds);
