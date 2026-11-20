using System.Collections.Concurrent;
using PhotoManager.Core.Domain;
using PhotoManager.Worker.Activities;
using Temporalio.Activities;
using Temporalio.Exceptions;

namespace PhotoManager.Worker.Tests.Workflows;

/// <summary>
/// Stand-ins registered under the real activity names, so workflow logic is tested without storage, crypto,
/// Gemini or a database. Each fake records its calls.
/// </summary>
public sealed class FakeActivities
{
    public ConcurrentQueue<string> Calls { get; } = new();

    public InspectResult Inspection { get; set; } = new("abc", "image/jpeg", null);

    public Exception? InspectError { get; set; }

    public Exception? DescribeError { get; set; }

    /// <summary>Failure of the staged rendition (the first full decode of an upload).</summary>
    public Exception? RenderError { get; set; }

    /// <summary>What the staged describe and render activities were asked to verify.</summary>
    public ConcurrentQueue<StagedUploadInput> StagedInputs { get; } = new();

    public Exception? ArchiveError { get; set; }

    public ArchiveOriginalInput? ArchivedWith { get; private set; }

    public IngestOutcomeInput? Outcome { get; private set; }

    public CompleteIngestInput? Completed { get; private set; }

    public Queue<IndexPageResult> Pages { get; } = new();

    public SweepInput? Sweep { get; private set; }

    public List<JobProgressInput> Progress { get; } = [];

    public Queue<IReadOnlyList<Guid>> Candidates { get; } = new();

    /// <summary>Photos whose rendering fails because the image format is unsupported.</summary>
    public HashSet<Guid> FailingPhotos { get; } = [];

    /// <summary>Photos whose rendering fails for another (non-unsupported) reason.</summary>
    public HashSet<Guid> BrokenPhotos { get; } = [];

    /// <summary>Photos that turn out to be identical copies of another photo.</summary>
    public HashSet<Guid> DuplicatePhotos { get; } = [];

    public PhotoStatus CompleteResult { get; set; } = PhotoStatus.Ready;

    public ConcurrentQueue<DescribeArchivedInput> DescribedArchived { get; } = new();

    [Activity("InspectStagedUpload")]
    public InspectResult Inspect(Guid photoId)
    {
        Calls.Enqueue("inspect");
        return InspectError is null ? Inspection : throw InspectError;
    }

    [Activity("DescribeStagedUpload")]
    public DescribeResult Describe(StagedUploadInput input)
    {
        Calls.Enqueue("describe");
        StagedInputs.Enqueue(input);
        return DescribeError is null ? new DescribeResult("woman and boy smiling on the beach", ["beach"], "test-model") : throw DescribeError;
    }

    [Activity("CreateRenditionsFromStaging")]
    public void RenderStaged(StagedUploadInput input)
    {
        Calls.Enqueue("render");
        StagedInputs.Enqueue(input);
        if (RenderError is not null)
        {
            throw RenderError;
        }
    }

    [Activity("ArchiveOriginal")]
    public ArchiveResult Archive(ArchiveOriginalInput input)
    {
        Calls.Enqueue("archive");
        ArchivedWith = input;
        return ArchiveError is null ? new ArchiveResult("v2/2026/11/x.pmv2", 123, "etag") : throw ArchiveError;
    }

    [Activity("CompleteIngest")]
    public PhotoStatus Complete(CompleteIngestInput input)
    {
        Calls.Enqueue("complete");
        Completed = input;
        return CompleteResult;
    }

    [Activity("FinishIngestWithoutArchive")]
    public void Finish(IngestOutcomeInput input)
    {
        Calls.Enqueue("finish");
        Outcome = input;
    }

    [Activity("MarkRenditionsFailed")]
    public void MarkRenditionsFailed(Guid photoId) => Calls.Enqueue($"renditions-failed:{photoId}");

    [Activity("MarkDescriptionFailed")]
    public void MarkDescriptionFailed(Guid photoId) => Calls.Enqueue($"description-failed:{photoId}");

    [Activity("EnsureJob")]
    public Guid EnsureJob(EnsureJobInput input) => input.JobId;

    [Activity("RecordJobProgress")]
    public void RecordProgress(JobProgressInput input)
    {
        lock (Progress)
        {
            Progress.Add(input);
        }
    }

    [Activity("IndexArchivePage")]
    public IndexPageResult IndexPage(string? continuationToken)
    {
        Calls.Enqueue($"page:{continuationToken ?? "start"}");
        return Pages.Dequeue();
    }

    [Activity("SweepMissing")]
    public SweepResult SweepMissing(SweepInput input)
    {
        Sweep = input;
        return new SweepResult(2, null);
    }

    [Activity("FindBackfillCandidates")]
    public IReadOnlyList<Guid> FindCandidates(BackfillQuery query) => Candidates.Count > 0 ? Candidates.Dequeue() : [];

    [Activity("CreateRenditionsFromArchive")]
    public RenditionOutcome RenderArchived(Guid photoId)
    {
        Calls.Enqueue($"render-archived:{photoId}");
        if (FailingPhotos.Contains(photoId))
        {
            throw new ApplicationFailureException("bad", "UnsupportedImage", nonRetryable: true);
        }

        if (BrokenPhotos.Contains(photoId))
        {
            throw Permanent("DecryptionFailed");
        }

        return DuplicatePhotos.Contains(photoId) ? RenditionOutcome.Duplicate : RenditionOutcome.Created;
    }

    [Activity("DescribeArchivedPhoto")]
    public void DescribeArchived(DescribeArchivedInput input)
    {
        Calls.Enqueue($"describe-archived:{input.PhotoId}");
        DescribedArchived.Enqueue(input);
    }

    public static ApplicationFailureException Permanent(string type) => new($"{type} for test", type, nonRetryable: true);
}
