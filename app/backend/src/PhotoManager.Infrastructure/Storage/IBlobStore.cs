namespace PhotoManager.Infrastructure.Storage;

/// <summary>Private blob storage for web-key-encrypted data.</summary>
public interface IBlobStore
{
    /// <summary>Opens a blob for reading.</summary>
    /// <param name="area">The storage area.</param>
    /// <param name="name">The blob name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A readable stream.</returns>
    /// <exception cref="StorageObjectNotFoundException">The blob does not exist.</exception>
    Task<Stream> OpenReadAsync(BlobArea area, string name, CancellationToken cancellationToken);

    /// <summary>Opens a blob for writing, replacing any existing content when the stream is disposed.</summary>
    /// <param name="area">The storage area.</param>
    /// <param name="name">The blob name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A writable stream; the blob is committed when it is disposed.</returns>
    Task<Stream> OpenWriteAsync(BlobArea area, string name, CancellationToken cancellationToken);

    /// <summary>Uploads a complete buffer.</summary>
    /// <param name="area">The storage area.</param>
    /// <param name="name">The blob name.</param>
    /// <param name="content">The content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the upload is committed.</returns>
    Task UploadAsync(BlobArea area, string name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>Checks whether a blob exists.</summary>
    /// <param name="area">The storage area.</param>
    /// <param name="name">The blob name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether it exists.</returns>
    Task<bool> ExistsAsync(BlobArea area, string name, CancellationToken cancellationToken);

    /// <summary>Deletes a blob if it exists (idempotent).</summary>
    /// <param name="area">The storage area.</param>
    /// <param name="name">The blob name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the blob is gone.</returns>
    Task DeleteIfExistsAsync(BlobArea area, string name, CancellationToken cancellationToken);
}
