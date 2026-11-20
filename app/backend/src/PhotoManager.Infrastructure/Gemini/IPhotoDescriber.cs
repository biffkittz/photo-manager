namespace PhotoManager.Infrastructure.Gemini;

/// <summary>Produces a short natural-language description of a photo.</summary>
public interface IPhotoDescriber
{
    /// <summary>Gets the model identifier recorded with each description.</summary>
    string Model { get; }

    /// <summary>Describes a JPEG image.</summary>
    /// <param name="jpeg">A downscaled JPEG with all metadata (EXIF/GPS) removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sanitized description and tags.</returns>
    /// <exception cref="PhotoDescriptionRejectedException">Permanent failure; retrying will not help.</exception>
    /// <exception cref="GeminiTransientException">Transient failure; safe to retry (possibly after a delay).</exception>
    Task<PhotoDescription> DescribeAsync(ReadOnlyMemory<byte> jpeg, CancellationToken cancellationToken);
}
