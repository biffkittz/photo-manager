using System.Globalization;
using PhotoManager.Core.Domain;
using PhotoManager.Core.Workflows;
using PhotoManager.Worker.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace PhotoManager.Worker.Workflows;

/// <summary>
/// Generates missing renditions or descriptions for archived photos. Each run fetches one batch of ids, processes
/// it with bounded parallelism, records progress, then continues-as-new with a keyset cursor. Fan-out stays
/// small (Temporal best practice: batch work instead of one child workflow per item), history stays bounded,
/// and a failure on one photo is recorded on that photo instead of failing the job.
/// </summary>
[Workflow(WorkflowTypes.Backfill)]
public sealed class BackfillWorkflow
{
    private const int BatchSize = 25;
    private const int Parallelism = 5;
    private static readonly TimeSpan DescribeBudget = TimeSpan.FromMinutes(30);

    /// <summary>Runs (or continues) a backfill.</summary>
    /// <param name="input">Job, kind, cursor and counters.</param>
    /// <returns>Final counters.</returns>
    [WorkflowRun]
    public async Task<BackfillProgress> RunAsync(BackfillInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var progress = input.Progress ?? new BackfillProgress();
        if (input.Progress is null)
        {
            await Workflow.ExecuteActivityAsync(
                (MaintenanceActivities a) => a.EnsureJobAsync(new EnsureJobInput(input.JobId, input.Kind, Workflow.Info.WorkflowId, Workflow.Info.FirstExecutionRunId)),
                ActivityProfiles.Database());
        }

        try
        {
            var batch = await Workflow.ExecuteActivityAsync(
                (MaintenanceActivities a) => a.FindBackfillCandidatesAsync(new BackfillQuery(input.Kind, input.AfterPhotoId, BatchSize)),
                ActivityProfiles.Database());

            if (batch.Count == 0)
            {
                var summary = string.Create(CultureInfo.InvariantCulture, $"Processed {progress.Processed} photos: {progress.Succeeded} succeeded, {progress.Failed} failed.");
                if (progress.Duplicates > 0)
                {
                    summary += string.Create(CultureInfo.InvariantCulture, $" {progress.Duplicates} were identical copies of photos already in the library and are now hidden.");
                }

                await RecordAsync(input.JobId, JobStatus.Completed, progress, summary, detached: false);
                return progress;
            }

            foreach (var group in batch.Chunk(Parallelism))
            {
                var results = await Workflow.WhenAllAsync(group.Select(id => ProcessAsync(input.Kind, id)));
                progress = new BackfillProgress(
                    progress.Processed + results.Length,
                    progress.Succeeded + results.Count(r => r == ItemResult.Succeeded),
                    progress.Failed + results.Count(r => r == ItemResult.Failed),
                    progress.Duplicates + results.Count(r => r == ItemResult.Duplicate));
            }

            await RecordAsync(input.JobId, JobStatus.Running, progress, summary: null, detached: false);
            var next = input with { AfterPhotoId = batch[^1], Progress = progress };
            throw Workflow.CreateContinueAsNewException((BackfillWorkflow wf) => wf.RunAsync(next));
        }
        catch (Exception ex) when (TemporalException.IsCanceledException(ex))
        {
            await RecordAsync(input.JobId, JobStatus.Cancelled, progress, "Cancelled by a user.", detached: true);
            throw;
        }
        catch (ActivityFailureException ex)
        {
            await RecordAsync(input.JobId, JobStatus.Failed, progress, $"Backfill failed: {ActivityErrors.UserMessage(ex)}", detached: true);
            throw;
        }
    }

    private enum ItemResult
    {
        Succeeded,
        Failed,
        Duplicate,
    }

    private static async Task<ItemResult> ProcessAsync(JobKind kind, Guid photoId)
    {
        try
        {
            if (kind == JobKind.BackfillRenditions)
            {
                var outcome = await Workflow.ExecuteActivityAsync((RenditionActivities a) => a.CreateRenditionsFromArchiveAsync(photoId), ActivityProfiles.Renditions());
                return outcome == RenditionOutcome.Duplicate ? ItemResult.Duplicate : ItemResult.Succeeded;
            }

            // A backfill never replaces a description someone wrote by hand.
            var describeInput = new DescribeArchivedInput(photoId, KeepUserDescription: true);
            await Workflow.ExecuteActivityAsync((DescribeActivities a) => a.DescribeArchivedPhotoAsync(describeInput), ActivityProfiles.Describe(DescribeBudget));
            return ItemResult.Succeeded;
        }
        catch (ActivityFailureException ex) when (!TemporalException.IsCanceledException(ex))
        {
            if (kind == JobKind.BackfillRenditions)
            {
                // Unsupported images were already recorded as such; marking them failed would retry them forever.
                if (!ActivityErrors.Is(ex, ActivityErrors.UnsupportedImage))
                {
                    await Workflow.ExecuteActivityAsync((RenditionActivities a) => a.MarkRenditionsFailedAsync(photoId), ActivityProfiles.Database());
                }
            }
            else
            {
                await Workflow.ExecuteActivityAsync((DescribeActivities a) => a.MarkDescriptionFailedAsync(photoId), ActivityProfiles.Database());
            }

            return ItemResult.Failed;
        }
    }

    private static Task RecordAsync(Guid jobId, JobStatus status, BackfillProgress progress, string? summary, bool detached) =>
        Workflow.ExecuteActivityAsync(
            (MaintenanceActivities a) => a.RecordJobProgressAsync(new JobProgressInput(jobId, status, progress.Processed, progress.Succeeded, progress.Failed, summary)),
            detached ? ActivityProfiles.DatabaseDetached() : ActivityProfiles.Database());
}
