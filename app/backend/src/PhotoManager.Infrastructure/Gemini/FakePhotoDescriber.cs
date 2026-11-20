namespace PhotoManager.Infrastructure.Gemini;

/// <summary>
/// Deterministic stand-in for local development and tests (<c>Gemini:UseFake=true</c>), so contributors can
/// run the whole pipeline without a Gemini key. Refuses to run outside Development (see worker startup).
/// </summary>
public sealed class FakePhotoDescriber : IPhotoDescriber
{
    /// <inheritdoc />
    public string Model => "fake-describer";

    /// <inheritdoc />
    public Task<PhotoDescription> DescribeAsync(ReadOnlyMemory<byte> jpeg, CancellationToken cancellationToken)
    {
        var size = jpeg.Length < 50_000 ? "small" : "large";
        return Task.FromResult(new PhotoDescription($"local development placeholder for a {size} photo", ["placeholder", size], Model));
    }
}
