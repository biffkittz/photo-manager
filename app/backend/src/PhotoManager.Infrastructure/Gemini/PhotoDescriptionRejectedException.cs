namespace PhotoManager.Infrastructure.Gemini;

/// <summary>The model permanently refused or could not describe the image (safety block, invalid request, bad credentials).</summary>
public sealed class PhotoDescriptionRejectedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PhotoDescriptionRejectedException"/> class.</summary>
    public PhotoDescriptionRejectedException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PhotoDescriptionRejectedException"/> class.</summary>
    /// <param name="message">Log-safe reason.</param>
    public PhotoDescriptionRejectedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PhotoDescriptionRejectedException"/> class.</summary>
    /// <param name="message">Log-safe reason.</param>
    /// <param name="innerException">Underlying exception.</param>
    public PhotoDescriptionRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
