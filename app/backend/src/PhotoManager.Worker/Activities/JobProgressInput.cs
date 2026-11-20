using PhotoManager.Core.Domain;

namespace PhotoManager.Worker.Activities;

/// <summary>Input of the RecordJobProgress activity.</summary>
/// <param name="JobId">The job.</param>
/// <param name="Status">Current status.</param>
/// <param name="Processed">Items processed.</param>
/// <param name="Succeeded">Items succeeded.</param>
/// <param name="Failed">Items failed.</param>
/// <param name="Summary">User-safe summary.</param>
public sealed record JobProgressInput(Guid JobId, JobStatus Status, int Processed, int Succeeded, int Failed, string? Summary);
