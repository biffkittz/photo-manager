namespace PhotoManager.Worker.Activities;

/// <summary>Input of the CompleteIngest activity.</summary>
/// <param name="PhotoId">The photo.</param>
/// <param name="Archive">The stored original.</param>
/// <param name="Description">The description, or null when Gemini failed or was skipped.</param>
/// <param name="DescriptionSkipped">True when the uploader switched descriptions off (the photo is then left "not
/// started" for a later description, rather than "failed").</param>
public sealed record CompleteIngestInput(Guid PhotoId, ArchiveResult Archive, DescribeResult? Description, bool DescriptionSkipped = false);
