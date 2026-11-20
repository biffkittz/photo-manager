using PhotoManager.Core.Domain;

namespace PhotoManager.Api.Contracts;

/// <summary>A maintenance job.</summary>
/// <param name="Id">Job id.</param>
/// <param name="Kind">Job kind.</param>
/// <param name="Status">Status.</param>
/// <param name="RequestedBy">Requester login or "schedule".</param>
/// <param name="CreatedAt">Creation time.</param>
/// <param name="StartedAt">Start time.</param>
/// <param name="CompletedAt">Completion time.</param>
/// <param name="Processed">Items processed.</param>
/// <param name="Succeeded">Items succeeded.</param>
/// <param name="Failed">Items failed.</param>
/// <param name="Summary">Outcome summary.</param>
public sealed record JobResponse(
    Guid Id,
    JobKind Kind,
    JobStatus Status,
    string RequestedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int Processed,
    int Succeeded,
    int Failed,
    string? Summary);
