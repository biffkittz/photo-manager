using PhotoManager.Core.Workflows;
using PhotoManager.Worker.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace PhotoManager.Worker.Workflows;

/// <summary>Prepares a download of one or more originals (decrypted by this worker, re-encrypted with the web key).</summary>
[Workflow(WorkflowTypes.PrepareDownload)]
public sealed class PrepareDownloadWorkflow
{
    /// <summary>Runs the workflow.</summary>
    /// <param name="input">The download request.</param>
    /// <returns>A task that completes when the download is ready or marked failed.</returns>
    [WorkflowRun]
    public async Task RunAsync(PrepareDownloadInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            await Workflow.ExecuteActivityAsync((DownloadActivities a) => a.PrepareDownloadAsync(input.DownloadId), ActivityProfiles.Download());
        }
        catch (ActivityFailureException ex)
        {
            await Workflow.ExecuteActivityAsync(
                (DownloadActivities a) => a.MarkDownloadFailedAsync(new DownloadFailedInput(input.DownloadId, ActivityErrors.UserMessage(ex))),
                ActivityProfiles.DatabaseDetached());
        }
    }
}
