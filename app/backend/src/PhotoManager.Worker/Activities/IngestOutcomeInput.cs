using PhotoManager.Core.Domain;

namespace PhotoManager.Worker.Activities;

/// <summary>Input of the FinishIngestWithoutArchive activity.</summary>
/// <param name="PhotoId">The photo.</param>
/// <param name="Status">Final status (Failed or Duplicate).</param>
/// <param name="Detail">User-safe explanation.</param>
/// <param name="DeleteStaging">Whether to delete the staged upload now (kept on retryable failures).</param>
public sealed record IngestOutcomeInput(Guid PhotoId, PhotoStatus Status, string Detail, bool DeleteStaging);
