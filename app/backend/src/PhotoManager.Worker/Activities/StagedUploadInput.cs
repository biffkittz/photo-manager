namespace PhotoManager.Worker.Activities;

/// <summary>Input of the ingest activities that read the staged upload after it was inspected.</summary>
/// <param name="PhotoId">The photo.</param>
/// <param name="ExpectedSha256">
/// Content hash computed by the inspection. The web tier can write to staging, so whatever is rendered, described
/// or archived must still hash to exactly what was inspected.
/// </param>
public sealed record StagedUploadInput(Guid PhotoId, string ExpectedSha256);
