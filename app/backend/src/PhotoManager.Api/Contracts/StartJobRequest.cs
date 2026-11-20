using PhotoManager.Core.Domain;

namespace PhotoManager.Api.Contracts;

/// <summary>Starts a maintenance job.</summary>
/// <param name="Kind">The job kind.</param>
/// <param name="Force">Re-index only: mark vanished photos missing even when more than the safety limit
/// disappeared (use after deliberately deleting many files from the archive).</param>
public sealed record StartJobRequest(JobKind Kind, bool Force = false);
