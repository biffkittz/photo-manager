using Microsoft.EntityFrameworkCore;
using PhotoManager.Api.Audit;
using PhotoManager.Api.Auth;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Security;
using PhotoManager.Core.Domain;
using PhotoManager.Core.Workflows;
using PhotoManager.Data;
using PhotoManager.Data.Entities;
using PhotoManager.Infrastructure.Temporal;
using Temporalio.Exceptions;

namespace PhotoManager.Api.Jobs;

/// <summary>Maintenance actions (admin only): re-index the archive and backfill renditions/descriptions.</summary>
public static class JobEndpoints
{
    /// <summary>Maps <c>/api/jobs/*</c>.</summary>
    /// <param name="api">The /api route group.</param>
    /// <returns>The same group.</returns>
    public static RouteGroupBuilder MapJobEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var jobs = api.MapGroup("/jobs").RequireAuthorization(PhotoRoles.AdminPolicy);
        jobs.MapGet("/", ListAsync);
        jobs.MapGet("/overview", OverviewAsync);
        jobs.MapPost("/", StartAsync);
        jobs.MapPost("/{id:guid}/cancel", CancelAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(PhotoDbContext db, CancellationToken ct)
    {
        var jobs = await db.Jobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Take(50)
            .Select(j => new JobResponse(j.Id, j.Kind, j.Status, j.RequestedBy, j.CreatedAt, j.StartedAt, j.CompletedAt, j.ProcessedCount, j.SucceededCount, j.FailedCount, j.Summary))
            .ToListAsync(ct);
        return TypedResults.Ok(jobs);
    }

    private static async Task<IResult> OverviewAsync(PhotoDbContext db, CancellationToken ct)
    {
        var ready = db.Photos.AsNoTracking().Where(p => p.Status == PhotoStatus.Ready);
        return TypedResults.Ok(new LibraryOverview(
            await ready.CountAsync(ct),
            await ready.CountAsync(p => p.RenditionState != ProcessingState.Ready, ct),
            await ready.CountAsync(p => p.DescriptionState != ProcessingState.Ready, ct),
            await db.Photos.CountAsync(p => p.Status == PhotoStatus.Failed, ct),
            await db.Photos.CountAsync(p => p.Status == PhotoStatus.Missing, ct)));
    }

    private static async Task<IResult> StartAsync(StartJobRequest request, HttpContext http, PhotoDbContext db, PhotoWorkflowClient workflows, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var workflowId = request.Kind switch
        {
            JobKind.ReindexArchive => WorkflowIds.ReindexArchive,
            JobKind.BackfillRenditions or JobKind.BackfillDescriptions => WorkflowIds.Backfill(request.Kind),
            _ => null,
        };
        if (workflowId is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Unknown job kind."] });
        }

        var job = new MaintenanceJob
        {
            Id = Guid.CreateVersion7(),
            Kind = request.Kind,
            Status = JobStatus.Queued,
            WorkflowId = workflowId,
            RequestedBy = http.User.Login(),
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        var force = request.Force && request.Kind == JobKind.ReindexArchive;
        string? runId;
        try
        {
            // Not tied to this request (see PhotoWorkflowClient): once the row exists, a closed browser tab must not
            // abandon the start halfway and leave a "Queued" job behind.
            runId = request.Kind == JobKind.ReindexArchive
                ? await workflows.StartReindexAsync(job.Id, force)
                : await workflows.StartBackfillAsync(job.Id, request.Kind);
        }
        catch (WorkflowAlreadyStartedException)
        {
            // Recorded rather than deleted: the web identity has no DELETE privilege on any table.
            await CloseUnstartedAsync(db, job.Id, JobStatus.Cancelled, "Not started: a job of this kind was already running.", time);
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "A job of this kind is already running.");
        }
        catch (Exception ex) when (TemporalUnavailable.Matches(ex))
        {
            // Never leave a "Queued" row behind that nothing will ever run. Should the start have reached Temporal
            // after all, the run refuses to work on a job that is already closed (MaintenanceActivities.EnsureJob).
            await CloseUnstartedAsync(db, job.Id, JobStatus.Failed, "Not started: background processing is unavailable. Try again shortly.", time);
            return TemporalUnavailable.Problem();
        }

        // The worker may already have picked the job up and marked it running (it records the same run id), which
        // changed the row's version: a tracked save would fail with a concurrency error after the job had started.
        // So only the run id is written, and only if nobody has written it yet.
        if (runId is not null)
        {
            var now = time.GetUtcNow();
            await db.Jobs.Where(j => j.Id == job.Id && j.RunId == null).ExecuteUpdateAsync(
                s => s.SetProperty(j => j.RunId, runId)
                      .SetProperty(j => j.UpdatedAt, now),
                CancellationToken.None);
        }

        await audit.WriteAsync("job.start", job.Id.ToString(), new { kind = request.Kind.ToString(), force });
        return TypedResults.Accepted($"/api/jobs/{job.Id}", new JobResponse(job.Id, job.Kind, job.Status, job.RequestedBy, job.CreatedAt, null, null, 0, 0, 0, null));
    }

    private static async Task<IResult> CancelAsync(Guid id, PhotoDbContext db, PhotoWorkflowClient workflows, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        if (job.Status is not (JobStatus.Queued or JobStatus.Running))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The job is not running.");
        }

        if (job.RunId is null)
        {
            // No run was ever recorded, so the start never reached Temporal. Job workflow ids are reused, and a cancel
            // without a run id would stop whatever job runs under this id now, so only the record is closed. A run
            // that starts after all refuses to work on a closed job (MaintenanceActivities.EnsureJob).
            var now = time.GetUtcNow();
            var closed = await db.Jobs.Where(j => j.Id == id && j.RunId == null && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running)).ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, JobStatus.Cancelled)
                      .SetProperty(j => j.Summary, "Stopped before it started.")
                      .SetProperty(j => j.CompletedAt, now)
                      .SetProperty(j => j.UpdatedAt, now),
                ct);
            if (closed == 0)
            {
                // It started (and recorded its run) in the meantime.
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The job has just started. Try again.");
            }

            await audit.WriteAsync("job.cancel", id.ToString(), null);
            return TypedResults.Accepted((string?)null);
        }

        try
        {
            await workflows.CancelAsync(job.WorkflowId, job.RunId, ct);
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            // The workflow already ended without recording it (or never started): close the stale record.
            var now = time.GetUtcNow();
            await db.Jobs.Where(j => j.Id == id && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running)).ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, JobStatus.Cancelled)
                      .SetProperty(j => j.Summary, "Stopped: the job was no longer running.")
                      .SetProperty(j => j.CompletedAt, now)
                      .SetProperty(j => j.UpdatedAt, now),
                ct);
        }
        catch (Exception ex) when (TemporalUnavailable.Matches(ex))
        {
            return TemporalUnavailable.Problem();
        }

        await audit.WriteAsync("job.cancel", id.ToString(), null);
        return TypedResults.Accepted((string?)null);
    }

    // Closes a job whose workflow never started. Conditional on Queued, so it never overwrites a status the worker set.
    private static Task<int> CloseUnstartedAsync(PhotoDbContext db, Guid jobId, JobStatus status, string summary, TimeProvider time)
    {
        var now = time.GetUtcNow();
        return db.Jobs.Where(j => j.Id == jobId && j.Status == JobStatus.Queued).ExecuteUpdateAsync(
            s => s.SetProperty(j => j.Status, status)
                  .SetProperty(j => j.Summary, summary)
                  .SetProperty(j => j.CompletedAt, now)
                  .SetProperty(j => j.UpdatedAt, now),
            CancellationToken.None);
    }
}
