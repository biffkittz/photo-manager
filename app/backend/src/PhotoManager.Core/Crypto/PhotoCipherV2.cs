using System.Security.Cryptography;
using System.Text.Json;

namespace PhotoManager.Core.Crypto;

/// <summary>
/// Entry points for the v2 authenticated encryption format.
/// </summary>
/// <remarks>
/// <code>
/// header[80]  magic | version | key type | reserved | chunk size | kdf iterations | key id | kdf salt | file salt | metadata length
/// metadata    AES-256-GCM(metadata key, nonce = 0, UTF-8 JSON, aad = header)   (optional)
/// chunk*      AES-256-GCM(content key, nonce = counter|final, plaintext chunk) ciphertext || tag
/// </code>
/// Per-file keys come from HKDF-SHA256(master key, salt = file salt, info = label || header). The full
/// specification, including the rationale for each choice, is in <c>docs/encryption.md</c>.
/// </remarks>
public static class PhotoCipherV2
{
    /// <summary>Default plaintext chunk size (64 KiB, as used by age and Tink streaming AEAD).</summary>
    public const int DefaultChunkSize = 64 * 1024;

    /// <summary>Size of the fixed header.</summary>
    public const int HeaderSize = V2Format.HeaderSize;

    /// <summary>Largest number of bytes a reader needs to fetch to obtain the header and metadata block.</summary>
    public const int MaxPrefixSize = V2Format.HeaderSize + V2Format.MaxMetadataPlaintextSize + V2Format.TagSize;

    /// <summary>Returns true when <paramref name="prefix"/> starts with the v2 magic bytes.</summary>
    /// <param name="prefix">The first bytes of a file.</param>
    /// <returns>Whether the bytes look like a v2 file.</returns>
    public static bool HasV2Magic(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= V2Format.Magic.Length && prefix[..V2Format.Magic.Length].SequenceEqual(V2Format.Magic);

    /// <summary>
    /// Writes the header (and encrypted metadata, if supplied) to <paramref name="destination"/> and returns a
    /// stream that encrypts everything written to it. Call <see cref="V2EncryptStream.CompleteAsync"/> when done.
    /// </summary>
    /// <param name="destination">Where ciphertext is written.</param>
    /// <param name="key">The master key to encrypt under.</param>
    /// <param name="metadata">Optional metadata sealed into the file.</param>
    /// <param name="chunkSize">Plaintext chunk size.</param>
    /// <param name="leaveOpen">Whether disposing the returned stream leaves <paramref name="destination"/> open.
    /// When false, ownership transfers immediately: <paramref name="destination"/> is also disposed if this method fails.</param>
    /// <param name="cancellationToken">Cancels writing the header.</param>
    /// <returns>The encrypting stream.</returns>
    public static async Task<V2EncryptStream> CreateEncryptStreamAsync(
        Stream destination,
        V2MasterKey key,
        EmbeddedPhotoMetadata? metadata = null,
        int chunkSize = DefaultChunkSize,
        bool leaveOpen = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, V2Format.MinChunkSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunkSize, V2Format.MaxChunkSize);

        var metadataJson = metadata is null
            ? null
            : JsonSerializer.SerializeToUtf8Bytes(metadata, CoreJsonContext.Default.EmbeddedPhotoMetadata);
        if (metadataJson is { Length: > V2Format.MaxMetadataPlaintextSize })
        {
            throw new ArgumentException("Embedded metadata exceeds 64 KiB.", nameof(metadata));
        }

        var header = new V2Header(
            key.KeyType,
            chunkSize,
            key.KdfIterations,
            key.KeyId.ToArray(),
            key.KdfSalt.ToArray(),
            RandomNumberGenerator.GetBytes(V2Format.FileSaltSize),
            metadataJson is null ? 0 : metadataJson.Length + V2Format.TagSize);
        var headerBytes = header.ToBytes();
        var (contentKey, metadataKey) = V2Format.DeriveFileKeys(key.KeyBytes, headerBytes);

        try
        {
            await destination.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
            if (metadataJson is not null)
            {
                var sealedMetadata = new byte[metadataJson.Length + V2Format.TagSize];
                using (var gcm = new AesGcm(metadataKey, V2Format.TagSize))
                {
                    // The metadata key is unique per file and used exactly once, so a zero nonce is safe.
                    gcm.Encrypt(
                        stackalloc byte[V2Format.NonceSize],
                        metadataJson,
                        sealedMetadata.AsSpan(0, metadataJson.Length),
                        sealedMetadata.AsSpan(metadataJson.Length),
                        headerBytes);
                }

                await destination.WriteAsync(sealedMetadata, cancellationToken).ConfigureAwait(false);
            }

            return new V2EncryptStream(destination, contentKey, chunkSize, leaveOpen);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(contentKey);
            if (!leaveOpen)
            {
                await destination.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(metadataKey);
        }
    }

    /// <summary>
    /// Reads and validates the header and metadata from <paramref name="source"/> and returns a stream
    /// of authenticated plaintext.
    /// </summary>
    /// <param name="source">The encrypted stream positioned at the start of the file.</param>
    /// <param name="keys">Resolves the master key named by the header.</param>
    /// <param name="leaveOpen">Whether disposing the returned stream leaves <paramref name="source"/> open. When
    /// false, ownership transfers immediately: <paramref name="source"/> is also disposed if this method fails.</param>
    /// <param name="expectedKeyType">When set, files encrypted with any other key type are rejected <em>before</em>
    /// any key is resolved, so data from a less trusted source can never trigger an archive-password derivation.</param>
    /// <param name="cancellationToken">Cancels reading the header.</param>
    /// <returns>The decrypting stream; its <see cref="V2DecryptStream.Metadata"/> is already verified.</returns>
    /// <exception cref="PhotoDecryptionException">The header or metadata is invalid, has an unexpected key type,
    /// or fails authentication.</exception>
    /// <exception cref="PhotoKeyUnavailableException">This process does not hold the required key.</exception>
    public static async Task<V2DecryptStream> OpenDecryptStreamAsync(
        Stream source,
        IV2KeyResolver keys,
        bool leaveOpen = false,
        V2KeyType? expectedKeyType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keys);

        try
        {
            var (header, _, contentKey, metadata) = await ReadPrefixAsync(source, keys, expectedKeyType, cancellationToken).ConfigureAwait(false);
            return new V2DecryptStream(source, contentKey, header, metadata, leaveOpen);
        }
        catch when (!leaveOpen)
        {
            await source.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Reads only the header and the authenticated metadata (no content is decrypted).</summary>
    /// <param name="source">The encrypted stream, or just its first <see cref="MaxPrefixSize"/> bytes.</param>
    /// <param name="keys">Resolves the master key named by the header.</param>
    /// <param name="expectedKeyType">When set, files encrypted with any other key type are rejected before any key is resolved.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The embedded metadata, or <see langword="null"/> when the file has none.</returns>
    public static async Task<EmbeddedPhotoMetadata?> ReadMetadataAsync(
        Stream source,
        IV2KeyResolver keys,
        V2KeyType? expectedKeyType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keys);

        var (_, _, contentKey, metadata) = await ReadPrefixAsync(source, keys, expectedKeyType, cancellationToken).ConfigureAwait(false);
        CryptographicOperations.ZeroMemory(contentKey);
        return metadata;
    }

    /// <summary>Convenience wrapper: encrypts all of <paramref name="plaintext"/> into <paramref name="destination"/>.</summary>
    /// <param name="plaintext">Data to encrypt.</param>
    /// <param name="destination">Receives the ciphertext; left open.</param>
    /// <param name="key">Master key.</param>
    /// <param name="metadata">Optional embedded metadata.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of plaintext bytes encrypted.</returns>
    public static async Task<long> EncryptAsync(
        Stream plaintext,
        Stream destination,
        V2MasterKey key,
        EmbeddedPhotoMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var encryptor = await CreateEncryptStreamAsync(destination, key, metadata, leaveOpen: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using (encryptor.ConfigureAwait(false))
        {
            await plaintext.CopyToAsync(encryptor, cancellationToken).ConfigureAwait(false);
            await encryptor.CompleteAsync(cancellationToken).ConfigureAwait(false);
            return encryptor.PlaintextLength;
        }
    }

    /// <summary>Convenience wrapper: decrypts all of <paramref name="source"/> into <paramref name="destination"/>.</summary>
    /// <param name="source">Encrypted data; left open.</param>
    /// <param name="destination">Receives authenticated plaintext.</param>
    /// <param name="keys">Key resolver.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The embedded metadata, if any.</returns>
    public static async Task<EmbeddedPhotoMetadata?> DecryptAsync(
        Stream source,
        Stream destination,
        IV2KeyResolver keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var decryptor = await OpenDecryptStreamAsync(source, keys, leaveOpen: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        await using (decryptor.ConfigureAwait(false))
        {
            await decryptor.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            return decryptor.Metadata;
        }
    }

    private static async Task<(V2Header Header, byte[] HeaderBytes, byte[] ContentKey, EmbeddedPhotoMetadata? Metadata)> ReadPrefixAsync(
        Stream source,
        IV2KeyResolver keys,
        V2KeyType? expectedKeyType,
        CancellationToken cancellationToken)
    {
        var headerBytes = new byte[V2Format.HeaderSize];
        await ReadExactlyAsync(source, headerBytes, "header", cancellationToken).ConfigureAwait(false);
        var header = V2Header.Parse(headerBytes);
        if (expectedKeyType is { } expected && header.KeyType != expected)
        {
            throw new PhotoDecryptionException($"Expected data encrypted with a {expected} key but found {header.KeyType}.");
        }

        var masterKey = keys.Resolve(header.KeyType, header.KeyId, header.KdfIterations, header.KdfSalt)
            ?? throw new PhotoKeyUnavailableException(
                $"No {header.KeyType} key with id {Convert.ToHexStringLower(header.KeyId)} is available in this process.");

        var (contentKey, metadataKey) = V2Format.DeriveFileKeys(masterKey.KeyBytes, headerBytes);
        try
        {
            EmbeddedPhotoMetadata? metadata = null;
            if (header.MetadataLength > 0)
            {
                var sealedMetadata = new byte[header.MetadataLength];
                await ReadExactlyAsync(source, sealedMetadata, "metadata", cancellationToken).ConfigureAwait(false);
                var json = new byte[header.MetadataLength - V2Format.TagSize];
                try
                {
                    using var gcm = new AesGcm(metadataKey, V2Format.TagSize);
                    gcm.Decrypt(
                        stackalloc byte[V2Format.NonceSize],
                        sealedMetadata.AsSpan(0, json.Length),
                        sealedMetadata.AsSpan(json.Length),
                        json,
                        headerBytes);
                }
                catch (AuthenticationTagMismatchException ex)
                {
                    throw new PhotoDecryptionException("Metadata authentication failed: the file is corrupted or the wrong key was used.", ex);
                }

                try
                {
                    metadata = JsonSerializer.Deserialize(json, CoreJsonContext.Default.EmbeddedPhotoMetadata);
                }
                catch (JsonException ex)
                {
                    // Authenticated but unparseable: written by a newer or broken writer. Report it like any other
                    // unreadable file instead of letting a serializer exception escape.
                    throw new PhotoDecryptionException("The embedded metadata is not valid.", ex);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(json);
                }
            }

            return (header, headerBytes, contentKey, metadata);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(contentKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(metadataKey);
        }
    }

    private static async Task ReadExactlyAsync(Stream source, byte[] buffer, string section, CancellationToken cancellationToken)
    {
        try
        {
            await source.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException ex)
        {
            throw new PhotoDecryptionException($"Encrypted file is truncated within its {section}.", ex);
        }
    }
}
