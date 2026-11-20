namespace PhotoManager.Infrastructure.Imaging;

/// <summary>The content is not a supported, safe-to-decode image. Permanent: retrying will not help.</summary>
public sealed class UnsupportedImageException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UnsupportedImageException"/> class.</summary>
    public UnsupportedImageException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsupportedImageException"/> class.</summary>
    /// <param name="message">User-safe reason.</param>
    public UnsupportedImageException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnsupportedImageException"/> class.</summary>
    /// <param name="message">User-safe reason.</param>
    /// <param name="innerException">Decoder exception.</param>
    public UnsupportedImageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
