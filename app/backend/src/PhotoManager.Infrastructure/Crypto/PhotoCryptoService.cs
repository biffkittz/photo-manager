using PhotoManager.Core.Crypto;

namespace PhotoManager.Infrastructure.Crypto;

/// <summary>
/// Format-aware encryption entry points used by the API and the worker. It enforces which key type each
/// kind of data must use: archive data is password-keyed, web data is raw-keyed. A blob carrying the
/// wrong key type is rejected even if this process happens to hold that key.
/// </summary>
/// <param name="keys">This process's key ring.</param>
public sealed class PhotoCryptoService(PhotoKeyRing keys)
{
    /// <summary>Gets a value indicating whether this process can read and write archive originals.</summary>
    public bool CanAccessArchive => keys.CanAccessArchive;

    /// <summary>Gets the id of the active web key (for diagnostics).</summary>
    public string ActiveWebKeyId => keys.ActiveWebKey.KeyIdHex;

    /// <summary>Opens a decrypting stream over an archive original (legacy or v2). Worker only.</summary>
    /// <param name="encrypted">The ciphertext stream; ownership transfers to the returned stream.</param>
    /// <param name="format">The archive format recorded for the photo.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A plaintext stream.</returns>
    public async Task<Stream> OpenArchiveDecryptStreamAsync(Stream encrypted, EncryptionFormat format, CancellationToken cancellationToken) =>
        format switch
        {
            EncryptionFormat.LegacyV1 => await LegacyPhotoCipher.OpenDecryptStreamAsync(encrypted, keys.GetLegacyArchivePassword(), cancellationToken).ConfigureAwait(false),
            EncryptionFormat.V2 => await OpenV2Async(encrypted, V2KeyType.Password, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown archive format."),
        };

    /// <summary>Opens a decrypting stream over web-key data (staging, renditions, downloads).</summary>
    /// <param name="encrypted">The ciphertext stream; ownership transfers to the returned stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A plaintext stream.</returns>
    public Task<V2DecryptStream> OpenWebDecryptStreamAsync(Stream encrypted, CancellationToken cancellationToken) =>
        OpenV2Async(encrypted, V2KeyType.RawKey, cancellationToken);

    /// <summary>Starts encrypting web-key data into <paramref name="destination"/>.</summary>
    /// <param name="destination">Ciphertext sink.</param>
    /// <param name="leaveOpen">Whether disposing the returned stream leaves <paramref name="destination"/> open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The encrypting stream; call <see cref="V2EncryptStream.CompleteAsync"/> when done.</returns>
    public Task<V2EncryptStream> CreateWebEncryptStreamAsync(Stream destination, bool leaveOpen, CancellationToken cancellationToken) =>
        PhotoCipherV2.CreateEncryptStreamAsync(destination, keys.ActiveWebKey, leaveOpen: leaveOpen, cancellationToken: cancellationToken);

    /// <summary>Encrypts a small buffer with the web key (renditions), returning the complete ciphertext.</summary>
    /// <param name="plaintext">The data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The v2 ciphertext.</returns>
    public async Task<byte[]> EncryptWithWebKeyAsync(ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(plaintext.Length + 256);
        var encryptor = await CreateWebEncryptStreamAsync(buffer, leaveOpen: true, cancellationToken).ConfigureAwait(false);
        await using (encryptor.ConfigureAwait(false))
        {
            await encryptor.WriteAsync(plaintext, cancellationToken).ConfigureAwait(false);
            await encryptor.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    /// <summary>Starts encrypting an archive original with embedded metadata. Worker only.</summary>
    /// <param name="destination">Ciphertext sink.</param>
    /// <param name="metadata">Metadata sealed into the file.</param>
    /// <param name="leaveOpen">Whether disposing the returned stream leaves <paramref name="destination"/> open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The encrypting stream; call <see cref="V2EncryptStream.CompleteAsync"/> when done.</returns>
    public Task<V2EncryptStream> CreateArchiveEncryptStreamAsync(Stream destination, EmbeddedPhotoMetadata metadata, bool leaveOpen, CancellationToken cancellationToken) =>
        PhotoCipherV2.CreateEncryptStreamAsync(destination, keys.ActiveArchiveKey, metadata, leaveOpen: leaveOpen, cancellationToken: cancellationToken);

    /// <summary>Reads the metadata sealed in a v2 archive file from its first bytes. Worker only.</summary>
    /// <param name="prefix">At least the header and metadata block of the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The metadata, if present.</returns>
    public Task<EmbeddedPhotoMetadata?> ReadArchiveMetadataAsync(Stream prefix, CancellationToken cancellationToken) =>
        PhotoCipherV2.ReadMetadataAsync(prefix, keys, V2KeyType.Password, cancellationToken);

    // The key type is checked from the header before any key is resolved, so web-key data (which the
    // internet-facing tier can write) can never make a worker run a password derivation.
    private Task<V2DecryptStream> OpenV2Async(Stream encrypted, V2KeyType expected, CancellationToken cancellationToken) =>
        PhotoCipherV2.OpenDecryptStreamAsync(encrypted, keys, expectedKeyType: expected, cancellationToken: cancellationToken);
}
