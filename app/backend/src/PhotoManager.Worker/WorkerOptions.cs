namespace PhotoManager.Worker;

/// <summary>Worker tuning. Section <c>Worker</c>.</summary>
public sealed class WorkerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Worker";

    /// <summary>Gets or sets the largest plaintext image an activity will buffer in memory.</summary>
    public long MaxPlaintextBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>Gets or sets the concurrent activity slots on the ingest queue (bounded by memory: each holds one photo).</summary>
    public int IngestMaxConcurrentActivities { get; set; } = 4;

    /// <summary>Gets or sets the concurrent activity slots on the interactive queue.</summary>
    public int InteractiveMaxConcurrentActivities { get; set; } = 4;

    /// <summary>Gets or sets the concurrent activity slots on the batch queue.</summary>
    public int BatchMaxConcurrentActivities { get; set; } = 4;

    /// <summary>Gets or sets the concurrent Gemini calls per worker replica.</summary>
    public int AiMaxConcurrentActivities { get; set; } = 4;

    /// <summary>
    /// Gets or sets the Gemini call rate across <em>all</em> replicas. Temporal enforces this per task queue
    /// on the server (<c>MaxTaskQueueActivitiesPerSecond</c>), so scaling out cannot exceed the API quota.
    /// </summary>
    public double AiActivitiesPerSecond { get; set; } = 1.0;

    /// <summary>Gets or sets how long in-flight activities may finish when the container stops (keep below the ACA termination grace period).</summary>
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>Gets or sets a value indicating whether the worker registers the nightly re-index Temporal Schedule.</summary>
    public bool NightlyReindexEnabled { get; set; } = true;

    /// <summary>Gets or sets the cron expression (UTC) of the nightly re-index. Default 09:00 UTC = 03:00 US Central.</summary>
    public string NightlyReindexCron { get; set; } = "0 9 * * *";

    /// <summary>Gets or sets where archive ciphertext is staged before upload (defaults to the OS temp directory).</summary>
    public string? TempDirectory { get; set; }

    /// <summary>Gets or sets how long a prepared download stays available (the blob lifecycle rule deletes it after a day).</summary>
    public TimeSpan DownloadLifetime { get; set; } = TimeSpan.FromHours(23);

    /// <summary>Gets or sets the most photos a single ZIP download may contain.</summary>
    public int MaxPhotosPerDownload { get; set; } = 500;

    /// <summary>Gets or sets the largest total original size a single download (single file or ZIP) may contain (default 4 GiB).</summary>
    public long MaxDownloadBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the most photos the workers prepare for download in any 24 hours, across all users. Enforced
    /// here rather than in the API: it bounds how fast even a compromised web tier could have the archive
    /// decrypted for it (docs/adr/0001-split-keys.md).
    /// </summary>
    public int MaxDownloadPhotosPerDay { get; set; } = 2000;
}
