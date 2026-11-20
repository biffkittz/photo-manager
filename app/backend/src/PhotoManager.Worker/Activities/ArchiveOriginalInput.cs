namespace PhotoManager.Worker.Activities;

/// <summary>Input of the ArchiveOriginal activity.</summary>
/// <param name="PhotoId">The photo.</param>
/// <param name="Description">Description to embed, if one was produced.</param>
/// <param name="Tags">Tags to embed.</param>
public sealed record ArchiveOriginalInput(Guid PhotoId, string? Description, IReadOnlyList<string> Tags);
