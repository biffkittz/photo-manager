using PhotoManager.Core.Workflows;
using PhotoManager.Worker.Activities;
using Temporalio.Common;
using Temporalio.Workflows;

namespace PhotoManager.Worker.Workflows;

/// <summary>
/// Timeouts and retry policies per kind of activity, in one place. Every activity sets StartToClose (the
/// recommended always-set timeout), which is kept above the timeouts of the calls it makes (for example the
/// Gemini HTTP timeout). Retries are bounded, back off exponentially, and skip permanent error types.
/// New instances are returned each time because <see cref="ActivityOptions"/> is mutable.
/// </summary>
internal static class ActivityProfiles
{
    /// <summary>Short database bookkeeping.</summary>
    public static ActivityOptions Database() => new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(30),
        RetryPolicy = Retry(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), 10),
    };

    /// <summary>Database bookkeeping that must run even after the workflow was cancelled (recording the outcome).</summary>
    public static ActivityOptions DatabaseDetached()
    {
        var options = Database();
        options.CancellationToken = CancellationToken.None;
        return options;
    }

    /// <summary>
    /// Bookkeeping that must eventually succeed, such as recording the final state of an upload: it is retried
    /// until the database is back (no attempt limit) instead of leaving a photo stuck in an intermediate state.
    /// Permanent errors still stop it immediately.
    /// </summary>
    public static ActivityOptions DatabaseUntilDone() => new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(30),
        RetryPolicy = Retry(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), attempts: 0),
        CancellationToken = CancellationToken.None,
    };

    /// <summary>Decrypt + decode header of a staged upload.</summary>
    public static ActivityOptions Inspect() => new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(2),
        RetryPolicy = Retry(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1), 5),
    };

    /// <summary>Gemini call on the rate-limited AI task queue.</summary>
    /// <param name="giveUpAfter">Total time across retries before continuing without a description.</param>
    public static ActivityOptions Describe(TimeSpan giveUpAfter) => new()
    {
        TaskQueue = TaskQueues.Ai,

        // Above GeminiOptions.RequestTimeout (150 s) so the HTTP call, not Temporal, times out first.
        StartToCloseTimeout = TimeSpan.FromMinutes(3),
        ScheduleToCloseTimeout = giveUpAfter,
        RetryPolicy = Retry(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), 8),
    };

    /// <summary>Decode + resize + encrypt + upload renditions.</summary>
    public static ActivityOptions Renditions() => new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(5),
        RetryPolicy = Retry(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2), 5),
    };

    /// <summary>Re-encrypt and upload an original to Wasabi.</summary>
    public static ActivityOptions Archive() => new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(15),
        RetryPolicy = Retry(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2), 10),
    };

    /// <summary>Stream originals into a download; heartbeats per photo.</summary>
    public static ActivityOptions Download() => new()
    {
        StartToCloseTimeout = TimeSpan.FromHours(2),
        HeartbeatTimeout = TimeSpan.FromMinutes(2),
        RetryPolicy = Retry(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(2), 3),
    };

    /// <summary>List and index one bucket page.</summary>
    public static ActivityOptions IndexPage() => new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(15),
        HeartbeatTimeout = TimeSpan.FromMinutes(2),
        RetryPolicy = Retry(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2), 10),
    };

    // attempts = 0 means unlimited (Temporal semantics).
    private static RetryPolicy Retry(TimeSpan initial, TimeSpan maximum, int attempts) => new()
    {
        InitialInterval = initial,
        BackoffCoefficient = 2,
        MaximumInterval = maximum,
        MaximumAttempts = attempts,
        NonRetryableErrorTypes = ActivityErrors.PermanentTypes,
    };
}
