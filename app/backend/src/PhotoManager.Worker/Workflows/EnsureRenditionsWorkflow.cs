using PhotoManager.Core.Workflows;
using PhotoManager.Worker.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace PhotoManager.Worker.Workflows;

/// <summary>
/// Generates renditions for one archived photo on demand (a user opened a photo that has none yet).
/// Started with a deterministic ID and "use existing" conflict policy, so concurrent viewers share one run.
/// </summary>
[Workflow(WorkflowTypes.EnsureRenditions)]
public sealed class EnsureRenditionsWorkflow
{
    /// <summary>Runs the workflow.</summary>
    /// <param name="input">The photo.</param>
    /// <returns>A task that completes when renditions exist or have been marked failed.</returns>
    [WorkflowRun]
    public async Task RunAsync(PhotoWorkInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            await Workflow.ExecuteActivityAsync((RenditionActivities a) => a.CreateRenditionsFromArchiveAsync(input.PhotoId), ActivityProfiles.Renditions());
        }
        catch (ActivityFailureException ex) when (!ActivityErrors.Is(ex, ActivityErrors.UnsupportedImage))
        {
            // Marking failure (instead of failing the workflow) stops the UI from re-requesting forever.
            // Unsupported images were already recorded as such by the activity.
            await Workflow.ExecuteActivityAsync((RenditionActivities a) => a.MarkRenditionsFailedAsync(input.PhotoId), ActivityProfiles.Database());
        }
        catch (ActivityFailureException)
        {
            // Unsupported: nothing more to record.
        }
    }
}
