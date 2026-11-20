namespace PhotoManager.Worker.Activities;

/// <summary>Input of the CompleteIngest activity.</summary>
/// <param name="PhotoId">The photo.</param>
/// <param name="Archive">The stored original.</param>
/// <param name="Description">The description, or null when Gemini failed.</param>
public sealed record CompleteIngestInput(Guid PhotoId, ArchiveResult Archive, DescribeResult? Description);
