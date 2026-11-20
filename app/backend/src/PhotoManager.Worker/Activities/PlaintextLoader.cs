using System.Buffers;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using PhotoManager.Data.Entities;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Storage;

namespace PhotoManager.Worker.Activities;

/// <summary>Fetches and decrypts staged uploads and archive originals.</summary>
/// <param name="blobs">Private blob storage.</param>
/// <param name="archive">The Wasabi archive.</param>
/// <param name="crypto">Crypto service (this worker holds the archive key).</param>
/// <param name="options">Worker options.</param>
public sealed class PlaintextLoader(IBlobStore blobs, IArchiveStore archive, PhotoCryptoService crypto, IOptions<WorkerOptions> options)
{
    /// <summary>Loads a staged upload (web-key encrypted) into memory.</summary>
    /// <param name="photoId">The photo id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plaintext and its hash.</returns>
    public async Task<LoadedImage> LoadStagedAsync(Guid photoId, CancellationToken cancellationToken)
    {
        var plaintext = await OpenStagedAsync(photoId, cancellationToken).ConfigureAwait(false);
        await using (plaintext.ConfigureAwait(false))
        {
            return await BufferAsync(plaintext, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Opens a staged upload as a plaintext stream.</summary>
    /// <param name="photoId">The photo id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authenticated plaintext stream.</returns>
    public async Task<Stream> OpenStagedAsync(Guid photoId, CancellationToken cancellationToken)
    {
        var encrypted = await blobs.OpenReadAsync(BlobArea.Staging, BlobNames.Staging(photoId), cancellationToken).ConfigureAwait(false);
        return await crypto.OpenWebDecryptStreamAsync(encrypted, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads an archive original (legacy or v2) into memory.</summary>
    /// <param name="photo">The photo record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plaintext and its hash.</returns>
    public async Task<LoadedImage> LoadArchivedAsync(Photo photo, CancellationToken cancellationToken)
    {
        var plaintext = await OpenArchivedAsync(photo, cancellationToken).ConfigureAwait(false);
        await using (plaintext.ConfigureAwait(false))
        {
            return await BufferAsync(plaintext, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Opens an archive original as a plaintext stream (used for streaming downloads).</summary>
    /// <param name="photo">The photo record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Plaintext stream.</returns>
    public async Task<Stream> OpenArchivedAsync(Photo photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.ArchiveKey is null || photo.EncryptionFormat is not { } format)
        {
            throw ActivityErrors.Permanent(ActivityErrors.InvalidState, "The photo has not been archived yet.");
        }

        var encrypted = await archive.OpenReadAsync(photo.ArchiveKey, cancellationToken).ConfigureAwait(false);
        return await crypto.OpenArchiveDecryptStreamAsync(encrypted, format, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LoadedImage> BufferAsync(Stream plaintext, CancellationToken cancellationToken)
    {
        var max = options.Value.MaxPlaintextBytes;
        var buffer = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunk = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            while ((read = await plaintext.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > max)
                {
                    throw ActivityErrors.Permanent(ActivityErrors.TooLarge, $"The image exceeds the {max / (1024 * 1024)} MB processing limit.");
                }

                hash.AppendData(chunk, 0, read);
                buffer.Write(chunk, 0, read);
            }
        }
        catch
        {
            CryptographicOperations.ZeroMemory(buffer.GetBuffer());
            await buffer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk, clearArray: true);
        }

        buffer.Position = 0;
        return new LoadedImage(buffer, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
