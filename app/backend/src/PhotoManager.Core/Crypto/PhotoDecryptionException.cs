namespace PhotoManager.Core.Crypto;

/// <summary>
/// Raised when encrypted content cannot be decrypted: malformed or truncated input, failed
/// authentication (tampering/corruption), or a wrong key. Messages never contain key material
/// or plaintext and are safe to log.
/// </summary>
public sealed class PhotoDecryptionException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PhotoDecryptionException"/> class.</summary>
    public PhotoDecryptionException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PhotoDecryptionException"/> class.</summary>
    /// <param name="message">A log-safe description of the failure.</param>
    public PhotoDecryptionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PhotoDecryptionException"/> class.</summary>
    /// <param name="message">A log-safe description of the failure.</param>
    /// <param name="innerException">The underlying cryptographic or I/O exception.</param>
    public PhotoDecryptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
