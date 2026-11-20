namespace PhotoManager.Api.Contracts;

/// <summary>Counts shown on the Actions page.</summary>
/// <param name="TotalPhotos">Browsable photos.</param>
/// <param name="MissingRenditions">Photos without thumbnails.</param>
/// <param name="MissingDescriptions">Photos without descriptions.</param>
/// <param name="FailedUploads">Uploads that failed.</param>
/// <param name="MissingFromArchive">Photos whose archive object disappeared.</param>
public sealed record LibraryOverview(int TotalPhotos, int MissingRenditions, int MissingDescriptions, int FailedUploads, int MissingFromArchive);
