namespace PhotoManager.Infrastructure.Gemini;

/// <summary>A sanitized AI description.</summary>
/// <param name="Description">Short caption, for example "woman and boy smiling on the beach".</param>
/// <param name="Tags">Lowercase keywords.</param>
/// <param name="Model">The model that produced it.</param>
public sealed record PhotoDescription(string Description, IReadOnlyList<string> Tags, string Model);
