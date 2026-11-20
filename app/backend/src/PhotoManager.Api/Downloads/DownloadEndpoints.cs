using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
        IOptions<DownloadOptions> options,
        TimeProvider time,
        CancellationToken ct)
    {
        var limits = options.Value;

        // Check raw list length before deduplicating so a payload full of duplicate GUIDs cannot force a large HashSet allocation.
        if (request.PhotoIds is null || request.PhotoIds.Count == 0 || request.PhotoIds.Count > limits.MaxPhotosPerDownload)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["photoIds"] = [$"Select between 1 and {limits.MaxPhotosPerDownload} photos."] });
        }

        var ids = request.PhotoIds.Distinct().ToList();
        var now = time.GetUtcNow();
        var userId = http.User.UserId();
        var activePending = await db.Downloads.CountAsync(
            d => d.RequestedById == userId && d.Status == DownloadStatus.Pending && d.ExpiresAt > now,
            ct);
        if (activePending >= limits.MaxPendingDownloadsPerUser)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: $"You can prepare at most {limits.MaxPendingDownloadsPerUser} downloads at a time. Wait for one to finish and try again.");
        }

        // Photos that are no longer available (missing from the archive, hidden as duplicates) are skipped and
        // reported once the download is ready; only a request with nothing left to download is refused.
        var availableSizes = await db.Photos.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Status == PhotoStatus.Ready && p.ArchiveKey != null)
            .Select(p => p.SizeBytes ?? p.ArchiveSizeBytes ?? 0L)
            .ToListAsync(ct);
        if (availableSizes.Count == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["photoIds"] = ["None of the selected photos are available for download."] });
        }

        long estimatedBytes = 0;
        foreach (var photoBytes in availableSizes)
        {
            var size = Math.Max(0L, photoBytes);
            if (size > limits.MaxDownloadBytes - estimatedBytes)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["photoIds"] = [$"Selected photos exceed the {FormatByteLimit(limits.MaxDownloadBytes)} download size limit."],
                });
            }

            estimatedBytes += size;
        }

        var download = new PreparedDownload
        {
            Id = Guid.CreateVersion7(),
            Status = DownloadStatus.Pending,
            PhotoIds = ids,
            RequestedBy = http.User.Login(),
            RequestedById = userId,
            FileName = ids.Count == 1 ? "photo" : "photos.zip",
            ExpiresAt = now.AddDays(1),
        };
        db.Downloads.Add(download);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("photo.download.request", download.Id.ToString(), new { count = ids.Count });
        try
        {
            await workflows.StartPrepareDownloadAsync(download.Id);
        }
        catch (Exception ex) when (TemporalUnavailable.Matches(ex))
        {
            // Record the failure instead of leaving a download that stays "preparing" forever.
            var failedAt = time.GetUtcNow();
            await db.Downloads.Where(d => d.Id == download.Id && d.Status == DownloadStatus.Pending).ExecuteUpdateAsync(
                s => s.SetProperty(d => d.Status, DownloadStatus.Failed)
                      .SetProperty(d => d.Error, "The download could not be started. Try again shortly.")
                      .SetProperty(d => d.UpdatedAt, failedAt),
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

    // Downloads are private to their requester (matched by the immutable user id, never the name): other
    // signed-in users get 404, not 403, so ids do not leak.
    private static Task<PreparedDownload?> FindOwnAsync(Guid id, HttpContext http, PhotoDbContext db, CancellationToken ct)
    {
        var userId = http.User.UserId();
        return db.Downloads.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.RequestedById == userId && userId != 0, ct);
    }

    private static DownloadResponse ToResponse(PreparedDownload d) =>
        new(d.Id, d.Status, d.FileName, d.SizeBytes, d.Error, d.ExpiresAt, d.PhotoIds.Count - d.SkippedCount, d.SkippedCount);

    private static string FormatByteLimit(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):0.#} GB"),
        >= 1024L * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };
}
