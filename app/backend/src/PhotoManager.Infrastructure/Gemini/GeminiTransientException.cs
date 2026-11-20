namespace PhotoManager.Infrastructure.Gemini;

/// <summary>A transient Gemini failure (rate limit, server error, malformed output). Temporal retries it.</summary>
public sealed class GeminiTransientException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="GeminiTransientException"/> class.</summary>
    public GeminiTransientException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GeminiTransientException"/> class.</summary>
    /// <param name="message">Log-safe reason.</param>
    public GeminiTransientException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GeminiTransientException"/> class.</summary>
    /// <param name="message">Log-safe reason.</param>
    /// <param name="innerException">Underlying exception.</param>
    public GeminiTransientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the delay requested by the server (HTTP Retry-After), if any.</summary>
    public TimeSpan? RetryAfter { get; init; }
}
