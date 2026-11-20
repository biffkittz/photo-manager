namespace PhotoManager.Core.Crypto;

/// <summary>
/// Identifies the on-disk format of an encrypted object (archive original, rendition, staged upload,
/// or prepared download). The numeric values are persisted in the database and must never change.
/// </summary>
public enum EncryptionFormat
{
    /// <summary>
    /// Format written by the original <c>protect.ps1</c> script:
    /// <c>salt(32) | IV(16) | AES-256-CBC/PKCS7 ciphertext</c>, key = PBKDF2-HMAC-SHA1(password, salt, 10,000).
    /// Read-only in this application; it has no integrity protection.
    /// </summary>
    LegacyV1 = 1,

    /// <summary>
    /// Authenticated, chunked AES-256-GCM format with a versioned header and optional encrypted
    /// metadata. Specified in <c>docs/encryption.md</c>.
    /// </summary>
    V2 = 2,
}
