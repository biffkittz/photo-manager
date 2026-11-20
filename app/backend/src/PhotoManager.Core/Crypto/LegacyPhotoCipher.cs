using System.Security.Cryptography;

namespace PhotoManager.Core.Crypto;

/// <summary>
/// Byte-compatible implementation of the encryption used by <c>protect.ps1</c>
/// (<c>Encrypt-FileBytes</c>/<c>Decrypt-FileBytes</c>), which produced every object already in the
/// <c>photo-bk</c> bucket.
/// </summary>
/// <remarks>
/// <para>File layout: <c>salt[32] | iv[16] | AES-256-CBC(PKCS7) ciphertext</c>.</para>
/// <para>
/// Key derivation: the script calls <c>new Rfc2898DeriveBytes(password, salt, 10000)</c>. That
/// constructor defaults to HMAC-SHA1 and encodes the password as UTF-8, so the equivalent call here is
/// <c>Rfc2898DeriveBytes.Pbkdf2(password, salt, 10000, HashAlgorithmName.SHA1, 32)</c>
/// with <see cref="HashAlgorithmName.SHA1"/>. SHA-1 and 10,000 iterations are weak by modern
/// standards, which is why new uploads use <see cref="PhotoCipherV2"/> instead. This class exists so
/// the existing archive stays readable, and so tests and the offline CLI can produce compatible files.
/// </para>
/// <para>
/// The format has no authentication tag: tampering is only detected if it happens to break the
/// PKCS7 padding. Callers must treat decrypted legacy content as untrusted input (it is only ever
/// handed to the image decoder, which validates it).
/// </para>
/// </remarks>
public static class LegacyPhotoCipher
{
    /// <summary>Size of the random PBKDF2 salt at the start of the file.</summary>
    public const int SaltSize = 32;

    /// <summary>Size of the AES-CBC initialization vector that follows the salt.</summary>
    public const int IvSize = 16;

    /// <summary>Total size of the plaintext header (salt + IV).</summary>
    public const int HeaderSize = SaltSize + IvSize;

    /// <summary>PBKDF2 iteration count hard-coded in <c>protect.ps1</c>.</summary>
    public const int Pbkdf2Iterations = 10_000;

    private const int KeySize = 32;

    /// <summary>
    /// Reads the legacy header from <paramref name="source"/> and returns a stream that yields the
    /// decrypted bytes. The returned stream owns <paramref name="source"/> and disposes it; if this method
    /// fails, <paramref name="source"/> is disposed before the exception propagates.
    /// </summary>
    /// <param name="source">The encrypted stream positioned at the start of the file.</param>
    /// <param name="password">The archive password (the same one passed to <c>protect.ps1</c>).</param>
    /// <param name="cancellationToken">Cancels reading the header.</param>
    /// <returns>A read-only stream of plaintext. A wrong password or corrupted file surfaces as a
    /// <see cref="CryptographicException"/> when the final block is read.</returns>
    /// <exception cref="PhotoDecryptionException">The input is shorter than the header.</exception>
    public static async Task<Stream> OpenDecryptStreamAsync(
        Stream source,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var header = new byte[HeaderSize];
        try
        {
            try
            {
                await source.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException ex)
            {
                throw new PhotoDecryptionException("Legacy file is shorter than its 48-byte header.", ex);
            }

            var key = DeriveKey(password, header.AsSpan(0, SaltSize));
            try
            {
                using var aes = Aes.Create();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                // The transform keeps its own copy of the key, so the Aes instance and our key buffer can be
                // released immediately. CryptoStream disposes the transform (and the source) when disposed.
                var transform = aes.CreateDecryptor(key, header[SaltSize..HeaderSize]);
                return new CryptoStream(source, transform, CryptoStreamMode.Read, leaveOpen: false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        catch
        {
            await source.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> into <paramref name="destination"/> using the exact
    /// <c>protect.ps1</c> format. Used by tests and the offline CLI; the application itself writes
    /// <see cref="EncryptionFormat.V2"/>.
    /// </summary>
    /// <param name="plaintext">The data to encrypt.</param>
    /// <param name="destination">Receives <c>salt | iv | ciphertext</c>. Left open.</param>
    /// <param name="password">The archive password.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>A task that completes when all data has been written.</returns>
    public static async Task EncryptAsync(
        Stream plaintext,
        Stream destination,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var key = DeriveKey(password, salt);
        try
        {
            await destination.WriteAsync(salt, cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(iv, cancellationToken).ConfigureAwait(false);

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var transform = aes.CreateEncryptor(key, iv);
            var crypto = new CryptoStream(destination, transform, CryptoStreamMode.Write, leaveOpen: true);
            await using (crypto.ConfigureAwait(false))
            {
                await plaintext.CopyToAsync(crypto, cancellationToken).ConfigureAwait(false);
                await crypto.FlushFinalBlockAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    // SHA-1 and 10k iterations are mandated by the existing archive format; see class remarks.
#pragma warning disable CA5379, CA5387
    private static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA1, KeySize);
#pragma warning restore CA5379, CA5387
}
