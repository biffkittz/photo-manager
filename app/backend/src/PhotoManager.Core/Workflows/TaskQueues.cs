namespace PhotoManager.Core.Workflows;

/// <summary>
/// Temporal task queue names, shared by the API (which starts workflows) and the worker (which polls).
/// Keep them as constants: a typo silently creates a second queue that no worker polls.
/// </summary>
/// <remarks>
/// Work is split by latency/resource profile so one workload cannot starve another (Temporal best practice):
/// interactive requests a user is waiting on never queue behind a bulk backfill, and Gemini calls get a
/// task-queue-wide rate limit that holds across all worker replicas.
/// </remarks>
public static class TaskQueues
{
    /// <summary>Per-photo upload ingestion (inspect, renditions, archive).</summary>
    public const string Ingest = "photo-ingest";

    /// <summary>Work a user is actively waiting on: download preparation, on-demand renditions.</summary>
    public const string Interactive = "photo-interactive";

    /// <summary>Bulk maintenance: archive re-indexing and backfills.</summary>
    public const string Batch = "photo-batch";

    /// <summary>Gemini calls only; rate limited with <c>MaxTaskQueueActivitiesPerSecond</c>.</summary>
    public const string Ai = "photo-ai";
}
