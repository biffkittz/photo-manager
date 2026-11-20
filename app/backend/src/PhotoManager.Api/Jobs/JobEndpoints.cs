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

    private static async Task<IResult> StartAsync(StartJobRequest request, HttpContext http, PhotoDbContext db, PhotoWorkflowClient workflows, AuditLog audit, CancellationToken ct)
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
        try
        {
            job.RunId = request.Kind == JobKind.ReindexArchive
                ? await workflows.StartReindexAsync(job.Id, force, ct)
                : await workflows.StartBackfillAsync(job.Id, request.Kind, ct);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (WorkflowAlreadyStartedException)
        {
            // Recorded rather than deleted: the web identity has no DELETE privilege on any table.
            job.Status = JobStatus.Cancelled;
            job.Summary = "Not started: a job of this kind was already running.";
            await db.SaveChangesAsync(CancellationToken.None);
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "A job of this kind is already running.");
        }
        catch (Exception ex) when (TemporalUnavailable.Matches(ex))
        {
            // Never leave a "Queued" row behind that nothing will ever run (and that a later cancel could target).
            job.Status = JobStatus.Failed;
            job.Summary = "Not started: background processing is unavailable. Try again shortly.";
            await db.SaveChangesAsync(CancellationToken.None);
            return TemporalUnavailable.Problem();
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
}
