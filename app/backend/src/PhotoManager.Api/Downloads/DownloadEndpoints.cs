using Microsoft.EntityFrameworkCore;
using PhotoManager.Api.Audit;
using PhotoManager.Api.Auth;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Security;
using PhotoManager.Core.Domain;
using PhotoManager.Data;
using PhotoManager.Data.Entities;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Storage;
using PhotoManager.Infrastructure.Temporal;

namespace PhotoManager.Api.Downloads;

/// <summary>
/// Downloads of originals. The API cannot decrypt originals (split keys), so it asks a worker to prepare the
/// download, polls for completion, then streams the web-key-encrypted result, decrypting on the fly.
/// </summary>
public static class DownloadEndpoints
{
    private const int MaxPhotosPerDownload = 500;

    /// <summary>Maps <c>/api/downloads/*</c>.</summary>
    /// <param name="api">The /api route group.</param>
    /// <returns>The same group.</returns>
    public static RouteGroupBuilder MapDownloadEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var downloads = api.MapGroup("/downloads");
        downloads.MapPost("/", CreateAsync).RequireRateLimiting(RateLimitPolicies.Expensive);
        downloads.MapGet("/{id:guid}", GetAsync);
        downloads.MapGet("/{id:guid}/content", ContentAsync).RequireRateLimiting(RateLimitPolicies.Expensive);
        return api;
    }

    private static async Task<IResult> CreateAsync(
        CreateDownloadRequest request,
        HttpContext http,
        PhotoDbContext db,
        PhotoWorkflowClient workflows,
        AuditLog audit,
        TimeProvider time,
        CancellationToken ct)
    {
        var ids = request.PhotoIds?.Distinct().ToList() ?? [];
        if (ids.Count is 0 or > MaxPhotosPerDownload)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["photoIds"] = [$"Select between 1 and {MaxPhotosPerDownload} photos."] });
        }

        // Photos that are no longer available (missing from the archive, hidden as duplicates) are skipped and
        // reported once the download is ready; only a request with nothing left to download is refused.
        var available = await db.Photos.CountAsync(p => ids.Contains(p.Id) && p.Status == PhotoStatus.Ready && p.ArchiveKey != null, ct);
        if (available == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["photoIds"] = ["None of the selected photos are available for download."] });
        }

        var download = new PreparedDownload
        {
            Id = Guid.CreateVersion7(),
            Status = DownloadStatus.Pending,
            PhotoIds = ids,
            RequestedBy = http.User.Login(),
            RequestedById = http.User.GitHubId(),
            FileName = ids.Count == 1 ? "photo" : "photos.zip",
            ExpiresAt = time.GetUtcNow().AddDays(1),
        };
        db.Downloads.Add(download);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("photo.download.request", download.Id.ToString(), new { count = ids.Count });
        try
        {
            await workflows.StartPrepareDownloadAsync(download.Id, ct);
        }
        catch (Exception ex) when (TemporalUnavailable.Matches(ex))
        {
            // Record the failure instead of leaving a download that stays "preparing" forever.
            var now = time.GetUtcNow();
            await db.Downloads.Where(d => d.Id == download.Id && d.Status == DownloadStatus.Pending).ExecuteUpdateAsync(
                s => s.SetProperty(d => d.Status, DownloadStatus.Failed)
                      .SetProperty(d => d.Error, "The download could not be started. Try again shortly.")
                      .SetProperty(d => d.UpdatedAt, now),
                CancellationToken.None);
            return TemporalUnavailable.Problem();
        }

        return TypedResults.Accepted($"/api/downloads/{download.Id}", ToResponse(download));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, PhotoDbContext db, CancellationToken ct)
    {
        var download = await FindOwnAsync(id, http, db, ct);
        return download is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(download));
    }

    private static async Task<IResult> ContentAsync(
        Guid id,
        HttpContext http,
        PhotoDbContext db,
        IBlobStore blobs,
        PhotoCryptoService crypto,
        AuditLog audit,
        TimeProvider time,
        CancellationToken ct)
    {
        var download = await FindOwnAsync(id, http, db, ct);
        if (download is null || download.Status != DownloadStatus.Ready || download.BlobName is null || download.ExpiresAt <= time.GetUtcNow())
        {
            return TypedResults.NotFound();
        }

        Stream plaintext;
        try
        {
            var encrypted = await blobs.OpenReadAsync(BlobArea.Downloads, download.BlobName, ct);

            // On failure the decryptor factory disposes the blob stream it was handed.
            plaintext = await crypto.OpenWebDecryptStreamAsync(encrypted, ct);
        }
        catch (StorageObjectNotFoundException)
        {
            return TypedResults.NotFound();
        }

        try
        {
            await audit.WriteAsync("photo.download", download.Id.ToString(), new { count = download.PhotoIds.Count - download.SkippedCount });
        }
        catch
        {
            await plaintext.DisposeAsync();
            throw;
        }

        // A known length lets the browser show progress, and a connection aborted by a decryption error mid-stream
        // is then unmistakably an incomplete download rather than a short file.
        if (download.SizeBytes is { } size)
        {
            http.Response.ContentLength = size;
        }

        // Attachment disposition + nosniff: the browser saves the file and never renders it inline.
        return Results.Stream(plaintext, download.ContentType, fileDownloadName: FileNames.Sanitize(download.FileName));
    }

    // Downloads are private to their requester (matched by the immutable GitHub id, never the login): other
    // signed-in users get 404, not 403, so ids do not leak.
    private static Task<PreparedDownload?> FindOwnAsync(Guid id, HttpContext http, PhotoDbContext db, CancellationToken ct)
    {
        var userId = http.User.GitHubId();
        return db.Downloads.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.RequestedById == userId && userId > 0, ct);
    }

    private static DownloadResponse ToResponse(PreparedDownload d) =>
        new(d.Id, d.Status, d.FileName, d.SizeBytes, d.Error, d.ExpiresAt, d.PhotoIds.Count - d.SkippedCount, d.SkippedCount);
}
