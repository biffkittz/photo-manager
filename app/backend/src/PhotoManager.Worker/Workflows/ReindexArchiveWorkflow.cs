using System.Globalization;
using PhotoManager.Core.Domain;
using PhotoManager.Core.Workflows;
using PhotoManager.Worker.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace PhotoManager.Worker.Workflows;

/// <summary>
/// Synchronizes the database with the archive bucket: one activity per 1000-object page, continue-as-new
/// every <see cref="MaxPagesPerRun"/> pages (or when Temporal suggests it) carrying only the continuation token
/// and counters, then a mark-and-sweep of objects that disappeared. Triggered from the Actions page and by
/// the nightly Temporal Schedule (which picks up files that protect.ps1 adds outside the app).
/// </summary>
[Workflow(WorkflowTypes.ReindexArchive)]
public sealed class ReindexArchiveWorkflow
{
    private const int MaxPagesPerRun = 20;

    // Activities stamp LastSeenAt with the worker clock while the scan start comes from the Temporal clock.
    // The margin absorbs clock skew; anything genuinely deleted was last seen days earlier anyway.
    private static readonly TimeSpan ClockSkewMargin = TimeSpan.FromMinutes(5);

    /// <summary>Runs (or continues) a re-index.</summary>
    /// <param name="input">Job, scan start, page token and counters.</param>
    /// <returns>Final counters.</returns>
    [WorkflowRun]
    public async Task<ReindexProgress> RunAsync(ReindexArchiveInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var scanStartedAt = input.ScanStartedAt ?? new DateTimeOffset(Workflow.UtcNow, TimeSpan.Zero);
        var jobId = input.JobId ?? Workflow.NewGuid();
        var progress = input.Progress ?? new ReindexProgress();
        var token = input.ContinuationToken;

        if (input.ScanStartedAt is null)
        {
            await Workflow.ExecuteActivityAsync(
                (MaintenanceActivities a) => a.EnsureJobAsync(new EnsureJobInput(jobId, JobKind.ReindexArchive, Workflow.Info.WorkflowId, Workflow.Info.FirstExecutionRunId)),
                ActivityProfiles.Database());
        }

        try
        {
            for (var page = 1; ; page++)
            {
                var result = await Workflow.ExecuteActivityAsync((MaintenanceActivities a) => a.IndexArchivePageAsync(token), ActivityProfiles.IndexPage());
                progress = progress.Add(result.Page);
                token = result.NextContinuationToken;
                await RecordAsync(jobId, JobStatus.Running, progress, summary: null, detached: false);

                if (token is null)
                {
                    break;
                }

                if (page >= MaxPagesPerRun || Workflow.ContinueAsNewSuggested)
                {
                    var next = input with { JobId = jobId, ScanStartedAt = scanStartedAt, ContinuationToken = token, Progress = progress };
                    throw Workflow.CreateContinueAsNewException((ReindexArchiveWorkflow wf) => wf.RunAsync(next));
                }
            }

            var sweep = await Workflow.ExecuteActivityAsync(
                (MaintenanceActivities a) => a.SweepMissingAsync(new SweepInput(scanStartedAt - ClockSkewMargin, progress.ObjectsSeen, input.Force)),
                ActivityProfiles.Database());
            progress = progress with { MarkedMissing = sweep.MarkedMissing };

            var summary = string.Create(
                CultureInfo.InvariantCulture,
                $"Scanned {progress.ObjectsSeen} photos: {progress.Added} added, {progress.Updated} updated, {progress.MarkedMissing} missing.");
            await RecordAsync(jobId, JobStatus.Completed, progress, sweep.Warning is null ? summary : $"{summary} {sweep.Warning}", detached: false);
            return progress;
        }
        catch (Exception ex) when (TemporalException.IsCanceledException(ex))
        {
            await RecordAsync(jobId, JobStatus.Cancelled, progress, "Cancelled by a user.", detached: true);
            throw;
        }
        catch (ActivityFailureException ex)
        {
            await RecordAsync(jobId, JobStatus.Failed, progress, $"Re-index failed: {ActivityErrors.UserMessage(ex)}", detached: true);
            throw;
        }
    }

    private static Task RecordAsync(Guid jobId, JobStatus status, ReindexProgress progress, string? summary, bool detached) =>
        Workflow.ExecuteActivityAsync(
            (MaintenanceActivities a) => a.RecordJobProgressAsync(new JobProgressInput(jobId, status, progress.ObjectsSeen, progress.Added + progress.Updated, 0, summary)),
            detached ? ActivityProfiles.DatabaseDetached() : ActivityProfiles.Database());
}
