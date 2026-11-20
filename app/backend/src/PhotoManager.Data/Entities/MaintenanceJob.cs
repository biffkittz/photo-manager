using PhotoManager.Core.Domain;

namespace PhotoManager.Data.Entities;

/// <summary>A long-running maintenance job (re-index, backfill) shown on the Actions page.</summary>
public sealed class MaintenanceJob : ITimestamped
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the job kind.</summary>
    public JobKind Kind { get; set; }

    /// <summary>Gets or sets the status.</summary>
    public JobStatus Status { get; set; }

    /// <summary>Gets or sets the Temporal workflow ID executing the job.</summary>
    public string WorkflowId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the run id of the first execution of this job's workflow chain. Job workflow ids are reused by
    /// later jobs of the same kind, so cancellation targets this run chain rather than "whatever runs under the id".
    /// </summary>
    public string? RunId { get; set; }

    /// <summary>Gets or sets the GitHub login of the requester, or <c>schedule</c>.</summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>Gets or sets when the job started running.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Gets or sets when the job finished.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Gets or sets how many items were processed.</summary>
    public int ProcessedCount { get; set; }

    /// <summary>Gets or sets how many items succeeded.</summary>
    public int SucceededCount { get; set; }

    /// <summary>Gets or sets how many items failed.</summary>
    public int FailedCount { get; set; }

    /// <summary>Gets or sets a short, user-safe summary of the outcome.</summary>
    public string? Summary { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token.</summary>
    public uint Version { get; set; }
}
