using System.Globalization;

namespace PhotoManager.Infrastructure.Storage;

/// <summary>Blob naming. Names contain only identifiers, never filenames or descriptions.</summary>
public static class BlobNames
{
    /// <summary>Gets the staged upload blob of a photo.</summary>
    /// <param name="photoId">The photo id.</param>
    /// <returns>The blob name.</returns>
    public static string Staging(Guid photoId) => photoId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>Gets the thumbnail blob of a photo.</summary>
    /// <param name="photoId">The photo id.</param>
    /// <returns>The blob name.</returns>
    public static string Thumbnail(Guid photoId) => $"{photoId:N}/thumbnail";

    /// <summary>Gets the preview blob of a photo.</summary>
    /// <param name="photoId">The photo id.</param>
    /// <returns>The blob name.</returns>
    public static string Preview(Guid photoId) => $"{photoId:N}/preview";

    /// <summary>Gets the blob of a prepared download.</summary>
    /// <param name="downloadId">The download id.</param>
    /// <returns>The blob name.</returns>
    public static string Download(Guid downloadId) => downloadId.ToString("N", CultureInfo.InvariantCulture);
}
