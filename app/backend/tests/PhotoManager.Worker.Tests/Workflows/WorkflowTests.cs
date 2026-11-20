using PhotoManager.Core.Domain;
using PhotoManager.Core.Workflows;
using PhotoManager.Worker.Activities;
using PhotoManager.Worker.Workflows;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;

namespace PhotoManager.Worker.Tests.Workflows;

/// <summary>Workflow logic on Temporal's time-skipping test server (timers and retries run instantly).</summary>
public sealed class WorkflowTests : IAsyncLifetime
{
    private WorkflowEnvironment? _env;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _env = await WorkflowEnvironment.StartTimeSkippingAsync();

    public async ValueTask DisposeAsync()
    {
        if (_env is not null)
        {
            await _env.ShutdownAsync();
        }
    }

    [Fact]
    public async Task Ingest_describes_before_archiving_and_embeds_the_description()
    {
        var fakes = new FakeActivities();
        var result = await RunAsync(fakes, (PhotoIngestWorkflow wf) => wf.RunAsync(new PhotoIngestInput(Guid.NewGuid())));

        Assert.Equal(PhotoStatus.Ready, result);
        var calls = fakes.Calls.ToList();
        Assert.True(calls.IndexOf("describe") < calls.IndexOf("archive"), string.Join(",", calls));
        Assert.Equal("woman and boy smiling on the beach", fakes.ArchivedWith!.Description);
        Assert.Equal("woman and boy smiling on the beach", fakes.Completed!.Description!.Description);
    }

    [Fact]
    public async Task Duplicate_ends_without_archiving_and_deletes_staging()
    {
        var fakes = new FakeActivities { Inspection = new("abc", "image/jpeg", Guid.NewGuid()) };
        var result = await RunAsync(fakes, (PhotoIngestWorkflow wf) => wf.RunAsync(new PhotoIngestInput(Guid.NewGuid())));

        Assert.Equal(PhotoStatus.Duplicate, result);
        Assert.DoesNotContain("archive", fakes.Calls);
        Assert.True(fakes.Outcome!.DeleteStaging);
    }

    [Fact]
    public async Task Unsupported_image_fails_fast_without_retrying()
    {
        var fakes = new FakeActivities { InspectError = FakeActivities.Permanent("UnsupportedImage") };
        var result = await RunAsync(fakes, (PhotoIngestWorkflow wf) => wf.RunAsync(new PhotoIngestInput(Guid.NewGuid())));

        Assert.Equal(PhotoStatus.Failed, result);
        Assert.Equal(1, fakes.Calls.Count(c => c == "inspect"));
        Assert.Equal("UnsupportedImage for test", fakes.Outcome!.Detail);
    }

    [Fact]
    public async Task Transient_inspection_failure_marks_failed_but_keeps_staging_for_retry()
    {
        // Not one of the permanent error types: the upload failed only because something was down, so the staged
        // copy must survive for the Retry button. (Non-retryable here only to keep the test fast.)
        var fakes = new FakeActivities { InspectError = new Temporalio.Exceptions.ApplicationFailureException("storage unavailable", "StorageUnavailable", nonRetryable: true) };
        var result = await RunAsync(fakes, (PhotoIngestWorkflow wf) => wf.RunAsync(new PhotoIngestInput(Guid.NewGuid())));

        Assert.Equal(PhotoStatus.Failed, result);
        Assert.False(fakes.Outcome!.DeleteStaging);
        Assert.Equal("Processing failed after several attempts. Try again later.", fakes.Outcome.Detail);
        Assert.DoesNotContain("archive", fakes.Calls);
    }

    [Fact]
    public async Task Identical_upload_completing_second_ends_as_duplicate()
    {
        var fakes = new FakeActivities { CompleteResult = PhotoStatus.Duplicate };
        var result = await RunAsync(fakes, (PhotoIngestWorkflow wf) => wf.RunAsync(new PhotoIngestInput(Guid.NewGuid())));

        Assert.Equal(PhotoStatus.Duplicate, result);
        Assert.Contains("complete", fakes.Calls);
    }

    [Fact]
    public async Task Gemini_failure_does_not_block_archiving()
    {
        var fakes = new FakeActivities { DescribeError = FakeActivities.Permanent("DescriptionRejected") };
        var result = await RunAsync(fakes, (PhotoIngestWorkflow wf) => wf.RunAsync(new PhotoIngestInput(Guid.NewGuid())));

        Assert.Equal(PhotoStatus.Ready, result);
        Assert.Null(fakes.ArchivedWith!.Description);
        Assert.Null(fakes.Completed!.Description);
    }

    [Fact]
    public async Task Archive_failure_marks_failed_but_keeps_staging_for_retry()
    {
        var fakes = new FakeActivities { ArchiveError = FakeActivities.Permanent("NotFound") };
        var result = await RunAsync(fakes, (PhotoIngestWorkflow wf) => wf.RunAsync(new PhotoIngestInput(Guid.NewGuid())));

        Assert.Equal(PhotoStatus.Failed, result);
        Assert.False(fakes.Outcome!.DeleteStaging);
        Assert.DoesNotContain("complete", fakes.Calls);
    }

    [Fact]
    public async Task Reindex_continues_as_new_across_many_pages_then_sweeps()
    {
        var fakes = new FakeActivities();
        const int pages = 45; // > 2 x MaxPagesPerRun, so the workflow continues-as-new twice
        for (var i = 1; i <= pages; i++)
        {
            fakes.Pages.Enqueue(new IndexPageResult(i < pages ? $"t{i}" : null, new ReindexProgress(1, 1000, 10, 1)));
        }

        var progress = await RunAsync(fakes, (ReindexArchiveWorkflow wf) => wf.RunAsync(new ReindexArchiveInput(Guid.NewGuid())), TaskQueues.Batch);

        Assert.Equal(pages, progress.Pages);
        Assert.Equal(pages * 1000, progress.ObjectsSeen);
        Assert.Equal(2, progress.MarkedMissing);
        Assert.Equal(pages * 1000, fakes.Sweep!.ObjectsSeen);
        Assert.False(fakes.Sweep.Force);
        Assert.Equal(Core.Domain.JobStatus.Completed, fakes.Progress[^1].Status);
    }

    [Fact]
    public async Task Reindex_force_flag_survives_continue_as_new_and_reaches_the_sweep()
    {
        var fakes = new FakeActivities();
        for (var i = 1; i <= 25; i++)
        {
            fakes.Pages.Enqueue(new IndexPageResult(i < 25 ? $"t{i}" : null, new ReindexProgress(1, 10, 0, 0)));
        }

        var input = new ReindexArchiveInput(Guid.NewGuid(), Force: true);
        await RunAsync(fakes, (ReindexArchiveWorkflow wf) => wf.RunAsync(input), TaskQueues.Batch);

        Assert.True(fakes.Sweep!.Force);
    }

    [Fact]
    public async Task Backfill_processes_batches_and_records_per_photo_failures()
    {
        var fakes = new FakeActivities();
        var first = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid()).ToList();
        var second = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        fakes.Candidates.Enqueue(first);
        fakes.Candidates.Enqueue(second);
        fakes.FailingPhotos.Add(first[7]);
        fakes.BrokenPhotos.Add(first[9]);
        fakes.DuplicatePhotos.Add(second[1]);

        var progress = await RunAsync(fakes, (BackfillWorkflow wf) => wf.RunAsync(new BackfillInput(Guid.NewGuid(), JobKind.BackfillRenditions)), TaskQueues.Batch);

        Assert.Equal(28, progress.Processed);
        Assert.Equal(25, progress.Succeeded);
        Assert.Equal(2, progress.Failed);
        Assert.Equal(1, progress.Duplicates);

        // Unsupported images keep their "Unsupported" state (marking them failed would retry them forever);
        // other failures are recorded so the next backfill retries them.
        Assert.DoesNotContain($"renditions-failed:{first[7]}", fakes.Calls);
        Assert.Contains($"renditions-failed:{first[9]}", fakes.Calls);
        Assert.Equal(Core.Domain.JobStatus.Completed, fakes.Progress[^1].Status);
        Assert.Contains("1 were identical copies", fakes.Progress[^1].Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Description_backfill_keeps_descriptions_people_wrote()
    {
        var fakes = new FakeActivities();
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        fakes.Candidates.Enqueue(ids);

        await RunAsync(fakes, (BackfillWorkflow wf) => wf.RunAsync(new BackfillInput(Guid.NewGuid(), JobKind.BackfillDescriptions)), TaskQueues.Batch);

        Assert.Equal(2, fakes.DescribedArchived.Count);
        Assert.All(fakes.DescribedArchived, input => Assert.True(input.KeepUserDescription));
    }

    [Fact]
    public async Task On_demand_renditions_leave_unsupported_photos_alone()
    {
        var fakes = new FakeActivities();
        var unsupported = Guid.NewGuid();
        fakes.FailingPhotos.Add(unsupported);

        await RunAsync(fakes, (EnsureRenditionsWorkflow wf) => wf.RunAsync(new PhotoWorkInput(unsupported)), TaskQueues.Interactive);

        Assert.DoesNotContain($"renditions-failed:{unsupported}", fakes.Calls);
    }

    private async Task<TResult> RunAsync<TWorkflow, TResult>(
        FakeActivities fakes,
        System.Linq.Expressions.Expression<Func<TWorkflow, Task<TResult>>> run,
        string taskQueue = TaskQueues.Ingest)
    {
        using var aiWorker = new TemporalWorker(_env!.Client, new TemporalWorkerOptions(TaskQueues.Ai).AddAllActivities(fakes));
        using var worker = new TemporalWorker(_env.Client, WorkerOptions(fakes, taskQueue));
        return await aiWorker.ExecuteAsync(() => worker.ExecuteAsync(() =>
            _env.Client.ExecuteWorkflowAsync(run, new WorkflowOptions($"test-{Guid.NewGuid():N}", taskQueue) { RunTimeout = TimeSpan.FromMinutes(5) })));
    }

    private async Task RunAsync<TWorkflow>(
        FakeActivities fakes,
        System.Linq.Expressions.Expression<Func<TWorkflow, Task>> run,
        string taskQueue)
    {
        using var worker = new TemporalWorker(_env!.Client, WorkerOptions(fakes, taskQueue));
        await worker.ExecuteAsync(() =>
            _env.Client.ExecuteWorkflowAsync(run, new WorkflowOptions($"test-{Guid.NewGuid():N}", taskQueue) { RunTimeout = TimeSpan.FromMinutes(5) }));
    }

    // Gemini activities run on their own task queue in production; tests serve them from a second worker.
    private static TemporalWorkerOptions WorkerOptions(FakeActivities fakes, string taskQueue) =>
        new TemporalWorkerOptions(taskQueue)
            .AddWorkflow<PhotoIngestWorkflow>()
            .AddWorkflow<ReindexArchiveWorkflow>()
            .AddWorkflow<BackfillWorkflow>()
            .AddWorkflow<EnsureRenditionsWorkflow>()
            .AddAllActivities(fakes);
}
