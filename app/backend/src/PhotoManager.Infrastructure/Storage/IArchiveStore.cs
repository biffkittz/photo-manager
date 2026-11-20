namespace PhotoManager.Infrastructure.Storage;

/// <summary>The encrypted photo archive (Wasabi bucket). Only the worker tier has credentials for it.</summary>
public interface IArchiveStore
{
    /// <summary>Lists one page of objects.</summary>
    /// <param name="continuationToken">Token from the previous page, or <see langword="null"/>.</param>
    /// <param name="maxKeys">Maximum objects per page (S3 caps this at 1000).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page.</returns>
    Task<ArchiveListPage> ListAsync(string? continuationToken, int maxKeys, CancellationToken cancellationToken);

    /// <summary>Opens an object for streaming reads.</summary>
    /// <param name="key">Object key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content stream (dispose it to release the connection).</returns>
    /// <exception cref="StorageObjectNotFoundException">The object does not exist.</exception>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Reads a byte range of an object (used to read v2 headers without downloading the photo).</summary>
    /// <param name="key">Object key.</param>
    /// <param name="offset">First byte.</param>
    /// <param name="length">Number of bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The content stream.</returns>
    Task<Stream> OpenReadRangeAsync(string key, long offset, long length, CancellationToken cancellationToken);

    /// <summary>Gets object attributes, or <see langword="null"/> when it does not exist.</summary>
    /// <param name="key">Object key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attributes.</returns>
    Task<ArchiveObjectInfo?> GetInfoAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads a local (already encrypted) file. The server verifies a Content-MD5 digest, so a corrupted
    /// transfer fails instead of silently storing bad ciphertext.
    /// </summary>
    /// <param name="key">Object key.</param>
    /// <param name="filePath">Local file containing ciphertext.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored object's attributes.</returns>
    Task<ArchiveObjectInfo> UploadFileAsync(string key, string filePath, CancellationToken cancellationToken);
}
