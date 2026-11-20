using PhotoManager.Core.Domain;
using PhotoManager.Core.Workflows;
using PhotoManager.Worker.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace PhotoManager.Worker.Workflows;

/// <summary>
/// Ingests one uploaded photo: inspect, then (concurrently) describe with Gemini and render thumbnails,
/// then encrypt into the archive with the description sealed inside, then mark it ready.
/// </summary>
/// <remarks>
/// Ordering is deliberate: the description must exist <em>before</em> the original is encrypted into the
/// archive because it is embedded in the encrypted file. Description and renditions are best effort (a photo
/// without them is still safely archived and can be backfilled later); archiving is not. Only identifiers
/// cross Temporal; image bytes stay in storage (Temporal payload limit is 2 MB and history should stay small).
/// </remarks>
[Workflow(WorkflowTypes.PhotoIngest)]
public sealed class PhotoIngestWorkflow
{
    // Uploads should not wait indefinitely on Gemini (a call can take minutes); missing descriptions can be
    // backfilled later from the Actions page.
    private static readonly TimeSpan DescribeBudget = TimeSpan.FromMinutes(12);

    /// <summary>Runs the ingest.</summary>
    /// <param name="input">The photo to ingest.</param>
    /// <returns>The final status of the photo.</returns>
    [WorkflowRun]
    public async Task<PhotoStatus> RunAsync(PhotoIngestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var photoId = input.PhotoId;

        InspectResult inspection;
        try
        {
            inspection = await Workflow.ExecuteActivityAsync(
                (IngestActivities a) => a.InspectStagedUploadAsync(photoId),
                ActivityProfiles.Inspect());
        }
        catch (ActivityFailureException ex)
        {
            // A permanent problem (not an image, unsupported format...) ends the upload. A transient one that
            // outlasted the retries (storage or database outage) keeps the staged copy so the user can retry.
            var permanent = ActivityErrors.IsPermanent(ex);
            return await FinishAsync(photoId, PhotoStatus.Failed, ActivityErrors.UserMessage(ex), deleteStaging: permanent);
        }

        if (inspection.DuplicateOf is not null)
        {
            return await FinishAsync(photoId, PhotoStatus.Duplicate, "This photo is already in the library.", deleteStaging: true);
        }

        var describeTask = Workflow.ExecuteActivityAsync(
            (DescribeActivities a) => a.DescribeStagedUploadAsync(photoId),
            ActivityProfiles.Describe(DescribeBudget));
        var renditionTask = Workflow.ExecuteActivityAsync(
            (RenditionActivities a) => a.CreateRenditionsFromStagingAsync(photoId),
            ActivityProfiles.Renditions());

        DescribeResult? description = null;
        try
        {
            description = await describeTask;
        }
        catch (ActivityFailureException ex)
        {
            Workflow.Logger.LogWarning(ex, "Describing photo {PhotoId} failed; continuing without a description", photoId);
        }

        try
        {
            await renditionTask;
        }
        catch (ActivityFailureException ex)
        {
            Workflow.Logger.LogWarning(ex, "Rendering photo {PhotoId} failed; continuing without renditions", photoId);
            await Workflow.ExecuteActivityAsync((RenditionActivities a) => a.MarkRenditionsFailedAsync(photoId), ActivityProfiles.Database());
        }

        // Built outside the lambda: activity calls are expression trees, which cannot contain ?. or [].
        var archiveInput = new ArchiveOriginalInput(photoId, description?.Description, description?.Tags ?? []);
        ArchiveResult archived;
        try
        {
            archived = await Workflow.ExecuteActivityAsync((IngestActivities a) => a.ArchiveOriginalAsync(archiveInput), ActivityProfiles.Archive());
        }
        catch (ActivityFailureException ex)
        {
            // Keep the staged upload so the user can retry from the Uploads page (staging expires after a day).
            return await FinishAsync(photoId, PhotoStatus.Failed, ActivityErrors.UserMessage(ex), deleteStaging: false);
        }

        // The original is safely archived at this point; recording that must not give up while the database is
        // unavailable, or the photo would stay "Processing" forever.
        return await Workflow.ExecuteActivityAsync(
            (IngestActivities a) => a.CompleteIngestAsync(new CompleteIngestInput(photoId, archived, description)),
            ActivityProfiles.DatabaseUntilDone());
    }

    private static async Task<PhotoStatus> FinishAsync(Guid photoId, PhotoStatus status, string detail, bool deleteStaging)
    {
        await Workflow.ExecuteActivityAsync(
            (IngestActivities a) => a.FinishIngestWithoutArchiveAsync(new IngestOutcomeInput(photoId, status, detail, deleteStaging)),
            ActivityProfiles.DatabaseUntilDone());
        return status;
    }
}
