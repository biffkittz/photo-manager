namespace PhotoManager.Worker.Activities;

/// <summary>A Gemini description returned to the ingest workflow (embedded into the archive file).</summary>
/// <param name="Description">Sanitized description.</param>
/// <param name="Tags">Sanitized tags.</param>
/// <param name="Model">Model id.</param>
public sealed record DescribeResult(string Description, IReadOnlyList<string> Tags, string Model);
