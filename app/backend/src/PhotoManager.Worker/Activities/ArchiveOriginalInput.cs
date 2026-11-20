namespace PhotoManager.Worker.Activities;

/// <summary>Input of the ArchiveOriginal activity.</summary>
/// <param name="PhotoId">The photo.</param>
/// <param name="ExpectedSha256">Content hash computed by the inspection; the archived bytes must match it.</param>
/// <param name="Description">Description to embed, if one was produced.</param>
/// <param name="Tags">Tags to embed.</param>
public sealed record ArchiveOriginalInput(Guid PhotoId, string ExpectedSha256, string? Description, IReadOnlyList<string> Tags);
