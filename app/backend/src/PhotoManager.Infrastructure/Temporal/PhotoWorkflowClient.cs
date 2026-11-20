using PhotoManager.Core.Domain;
using PhotoManager.Core.Workflows;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;

namespace PhotoManager.Infrastructure.Temporal;

/// <summary>
/// Typed facade the API uses to start and cancel workflows. Workflows are started by type name with
/// deterministic IDs (see <see cref="WorkflowIds"/>), so the API never references worker code and every
/// start is idempotent.
/// </summary>
/// <param name="client">The Temporal client.</param>
public sealed class PhotoWorkflowClient(ITemporalClient client)
{
    /// <summary>
    /// Starts ingesting an uploaded photo, or joins the run already in progress. A finished run may be followed by a
    /// new one (a retry after a failure); the workflow itself never downgrades a photo that is already ready.
    /// </summary>
    /// <param name="photoId">The photo id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the start is acknowledged.</returns>
    public Task StartIngestAsync(Guid photoId, CancellationToken cancellationToken) =>
        StartAsync(
            WorkflowTypes.PhotoIngest,
            new PhotoIngestInput(photoId),
            WorkflowIds.PhotoIngest(photoId),
            TaskQueues.Ingest,
            WorkflowIdReusePolicy.AllowDuplicate,
            WorkflowIdConflictPolicy.UseExisting,
            cancellationToken);

    /// <summary>Starts preparing a download.</summary>
    /// <param name="downloadId">The download id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the start is acknowledged.</returns>
    public Task StartPrepareDownloadAsync(Guid downloadId, CancellationToken cancellationToken) =>
        StartAsync(
            WorkflowTypes.PrepareDownload,
            new PrepareDownloadInput(downloadId),
            WorkflowIds.PrepareDownload(downloadId),
            TaskQueues.Interactive,
            WorkflowIdReusePolicy.RejectDuplicate,
            WorkflowIdConflictPolicy.UseExisting,
            cancellationToken);

    /// <summary>Starts (or joins) rendition generation for a photo. Many concurrent viewers coalesce into one execution.</summary>
    /// <param name="photoId">The photo id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the start is acknowledged.</returns>
    public Task StartEnsureRenditionsAsync(Guid photoId, CancellationToken cancellationToken) =>
        StartAsync(
            WorkflowTypes.EnsureRenditions,
            new PhotoWorkInput(photoId),
            WorkflowIds.EnsureRenditions(photoId),
            TaskQueues.Interactive,
            WorkflowIdReusePolicy.AllowDuplicate,
            WorkflowIdConflictPolicy.UseExisting,
            cancellationToken);

    /// <summary>Starts (or joins) regenerating a photo's AI description.</summary>
    /// <param name="photoId">The photo id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the start is acknowledged.</returns>
    public Task StartDescribeAsync(Guid photoId, CancellationToken cancellationToken) =>
        StartAsync(
            WorkflowTypes.DescribePhoto,
            new PhotoWorkInput(photoId),
            WorkflowIds.DescribePhoto(photoId),
            TaskQueues.Interactive,
            WorkflowIdReusePolicy.AllowDuplicate,
            WorkflowIdConflictPolicy.UseExisting,
            cancellationToken);

    /// <summary>Starts a re-index. Throws <see cref="Temporalio.Exceptions.WorkflowAlreadyStartedException"/> if one is running.</summary>
    /// <param name="jobId">The job record id.</param>
    /// <param name="force">Mark vanished photos missing even beyond the safety limit (admin-confirmed).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run id of the started workflow.</returns>
    public Task<string?> StartReindexAsync(Guid jobId, bool force, CancellationToken cancellationToken) =>
        StartAsync(
            WorkflowTypes.ReindexArchive,
            new ReindexArchiveInput(jobId, Force: force),
            WorkflowIds.ReindexArchive,
            TaskQueues.Batch,
            WorkflowIdReusePolicy.AllowDuplicate,
            WorkflowIdConflictPolicy.Fail,
            cancellationToken);

    /// <summary>Starts a backfill. Throws <see cref="Temporalio.Exceptions.WorkflowAlreadyStartedException"/> if one of the same kind is running.</summary>
    /// <param name="jobId">The job record id.</param>
    /// <param name="kind">Backfill kind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run id of the started workflow.</returns>
    public Task<string?> StartBackfillAsync(Guid jobId, JobKind kind, CancellationToken cancellationToken) =>
        StartAsync(
            WorkflowTypes.Backfill,
            new BackfillInput(jobId, kind),
            WorkflowIds.Backfill(kind),
            TaskQueues.Batch,
            WorkflowIdReusePolicy.AllowDuplicate,
            WorkflowIdConflictPolicy.Fail,
            cancellationToken);

    /// <summary>Requests cancellation of a workflow (the workflow cleans up and records the outcome).</summary>
    /// <param name="workflowId">The workflow id.</param>
    /// <param name="firstExecutionRunId">The run chain to cancel. Job workflow ids are reused by later jobs, so this
    /// keeps a cancel aimed at an old job from stopping a newer one; null targets whatever runs under the id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the request is acknowledged.</returns>
    public Task CancelAsync(string workflowId, string? firstExecutionRunId, CancellationToken cancellationToken) =>
        client.GetWorkflowHandle(workflowId, firstExecutionRunId: firstExecutionRunId)
            .CancelAsync(new WorkflowCancelOptions { Rpc = new RpcOptions { CancellationToken = cancellationToken } });

    private async Task<string?> StartAsync<TInput>(
        string workflowType,
        TInput input,
        string workflowId,
        string taskQueue,
        WorkflowIdReusePolicy reusePolicy,
        WorkflowIdConflictPolicy conflictPolicy,
        CancellationToken cancellationToken)
    {
        var handle = await client.StartWorkflowAsync(
            workflowType,
            [input],
            new WorkflowOptions(workflowId, taskQueue)
            {
                IdReusePolicy = reusePolicy,
                IdConflictPolicy = conflictPolicy,
                Rpc = new RpcOptions { CancellationToken = cancellationToken },
            }).ConfigureAwait(false);
        return handle?.FirstExecutionRunId ?? handle?.ResultRunId;
    }
}
