namespace PhotoManager.Core.Crypto;

/// <summary>
/// Raised when the current process does not hold the key needed for an operation. In the split-key
/// design this is expected (and desirable) when the web tier is asked to touch archive originals:
/// only the Temporal workers hold the archive password.
/// </summary>
public sealed class PhotoKeyUnavailableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PhotoKeyUnavailableException"/> class.</summary>
    public PhotoKeyUnavailableException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PhotoKeyUnavailableException"/> class.</summary>
    /// <param name="message">A log-safe description naming the missing key (never its value).</param>
    public PhotoKeyUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PhotoKeyUnavailableException"/> class.</summary>
    /// <param name="message">A log-safe description naming the missing key (never its value).</param>
    /// <param name="innerException">The underlying exception.</param>
    public PhotoKeyUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
