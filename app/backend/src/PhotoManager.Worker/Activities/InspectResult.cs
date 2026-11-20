namespace PhotoManager.Worker.Activities;

/// <summary>Outcome of inspecting a staged upload.</summary>
/// <param name="Sha256">Plaintext SHA-256 (hex).</param>
/// <param name="ContentType">Detected MIME type.</param>
/// <param name="DuplicateOf">An existing photo with identical content, if any.</param>
public sealed record InspectResult(string Sha256, string ContentType, Guid? DuplicateOf);
