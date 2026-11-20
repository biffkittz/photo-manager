namespace PhotoManager.Core.Workflows;

/// <summary>
/// Input (and continue-as-new state) of the <see cref="WorkflowTypes.ReindexArchive"/> workflow.
/// The workflow pages through the bucket and continues-as-new between batches of pages so its history stays small.
/// </summary>
/// <param name="JobId">Job record tracking this run; <see langword="null"/> when started by the schedule (the workflow creates one).</param>
/// <param name="ScanStartedAt">When the scan began (set on the first run). Photos not seen since then are swept as missing.</param>
/// <param name="ContinuationToken">S3 continuation token of the next page, or <see langword="null"/> to start.</param>
/// <param name="Progress">Counters carried across continue-as-new.</param>
/// <param name="Force">Mark vanished photos missing even when that exceeds the safety limit (an admin confirmed
/// that many files really were removed). Never set by the nightly schedule.</param>
public sealed record ReindexArchiveInput(
    Guid? JobId = null,
    DateTimeOffset? ScanStartedAt = null,
    string? ContinuationToken = null,
    ReindexProgress? Progress = null,
    bool Force = false);
