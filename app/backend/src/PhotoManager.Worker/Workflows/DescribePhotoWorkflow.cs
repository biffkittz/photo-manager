using PhotoManager.Core.Workflows;
using PhotoManager.Worker.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace PhotoManager.Worker.Workflows;

/// <summary>Regenerates the Gemini description of one archived photo (user request).</summary>
[Workflow(WorkflowTypes.DescribePhoto)]
public sealed class DescribePhotoWorkflow
{
    private static readonly TimeSpan DescribeBudget = TimeSpan.FromMinutes(15);

    /// <summary>Runs the workflow.</summary>
    /// <param name="input">The photo.</param>
    /// <returns>A task that completes when the description is stored or marked failed.</returns>
    [WorkflowRun]
    public async Task RunAsync(PhotoWorkInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            // Explicitly requested by a person for this photo, so it replaces whatever description exists.
            var describeInput = new DescribeArchivedInput(input.PhotoId, KeepUserDescription: false);
            await Workflow.ExecuteActivityAsync((DescribeActivities a) => a.DescribeArchivedPhotoAsync(describeInput), ActivityProfiles.Describe(DescribeBudget));
        }
        catch (ActivityFailureException)
        {
            await Workflow.ExecuteActivityAsync((DescribeActivities a) => a.MarkDescriptionFailedAsync(input.PhotoId), ActivityProfiles.Database());
        }
    }
}
