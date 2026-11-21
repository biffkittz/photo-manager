using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PhotoManager.Api.Audit;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Security;
using PhotoManager.Core.Crypto;
using PhotoManager.Core.Domain;
using PhotoManager.Data;
using PhotoManager.Data.Entities;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Gemini;
using PhotoManager.Infrastructure.Storage;
using PhotoManager.Infrastructure.Temporal;

namespace PhotoManager.Api.Photos;

/// <summary>Gallery, photo details, renditions and description editing.</summary>
public static class PhotoEndpoints
{
    private const int DefaultPageSize = 60;
    private const int MaxPageSize = 120;
    private const int MaxEditedDescriptionLength = 300;

    // "On this day" shows a strip of earlier years; a few hundred photos from one date is already a lot of scrolling.
    private const int MaxOnThisDayPhotos = 200;

    // Renditions are small WebP files (a preview is well under 1 MB); anything larger is not ours.
    private const int MaxRenditionBytes = 8 * 1024 * 1024;

    /// <summary>Maps <c>/api/photos/*</c>.</summary>
    /// <param name="api">The /api route group.</param>
    /// <returns>The same group.</returns>
    public static RouteGroupBuilder MapPhotoEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var photos = api.MapGroup("/photos");
        photos.MapGet("/", ListAsync);
        photos.MapGet("/timeline", TimelineAsync);
        photos.MapGet("/on-this-day", OnThisDayAsync);
        photos.MapGet("/{id:guid}", GetAsync);
        photos.MapPatch("/{id:guid}", UpdateAsync);
        photos.MapGet("/{id:guid}/thumbnail", (Guid id, HttpContext http, PhotoDbContext db, IBlobStore blobs, PhotoCryptoService crypto, PhotoWorkflowClient workflows, TimeProvider time, CancellationToken ct) =>
            ServeRenditionAsync(id, BlobNames.Thumbnail(id), http, db, blobs, crypto, workflows, time, ct));
        photos.MapGet("/{id:guid}/preview", (Guid id, HttpContext http, PhotoDbContext db, IBlobStore blobs, PhotoCryptoService crypto, PhotoWorkflowClient workflows, TimeProvider time, CancellationToken ct) =>
            ServeRenditionAsync(id, BlobNames.Preview(id), http, db, blobs, crypto, workflows, time, ct));
        photos.MapPost("/{id:guid}/describe", DescribeAsync).RequireRateLimiting(RateLimitPolicies.Expensive);
        return api;
    }

    private static async Task<IResult> ListAsync(
        PhotoDbContext db,
        string? q,
        string? cursor,
        int? limit,
        string? sort,
        int? year,
        int? month,
        int? offset,
        CancellationToken ct)
    {
        if (PhotoQueries.ValidateSearch(q) is { } searchErrors)
        {
            return TypedResults.ValidationProblem(searchErrors);
        }

        if (!PhotoQueries.TryUtcOffset(offset, out var utcOffset))
        {
            return TypedResults.ValidationProblem(PhotoQueries.OffsetError());
        }

        if (ValidatePeriod(year, month) is { } periodErrors)
        {
            return TypedResults.ValidationProblem(periodErrors);
        }

        GalleryCursor? after = null;
        if (cursor is not null && !GalleryCursor.TryParse(cursor, out after))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["cursor"] = ["The cursor is invalid."] });
        }

        var byTaken = string.Equals(sort, "taken", StringComparison.OrdinalIgnoreCase);
        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var query = PhotoQueries.Search(PhotoQueries.Library(db.Photos), q);

        // The timeline: a year, or one month of it, in the viewer's calendar. SortDate (taken, else uploaded) has an
        // index that starts with the status, so this is a range scan whichever order is chosen.
        if (year is { } periodYear)
        {
            var (from, to) = PhotoQueries.Period(periodYear, month, utcOffset);
            query = query.Where(p => p.SortDate >= from && p.SortDate < to);
        }

        // Keyset pagination: stable under concurrent inserts and O(page) regardless of depth, unlike OFFSET.
        // "Taken" order uses SortDate (= taken date, else upload date), a stored column with its own index.
        List<Photo> items;
        if (byTaken)
        {
            if (after is not null)
            {
                query = query.Where(p => p.SortDate < after.Time || (p.SortDate == after.Time && p.Id.CompareTo(after.Id) < 0));
            }

            items = await query.OrderByDescending(p => p.SortDate).ThenByDescending(p => p.Id).Take(size + 1).ToListAsync(ct);
        }
        else
        {
            if (after is not null)
            {
                query = query.Where(p => p.UploadedAt < after.Time || (p.UploadedAt == after.Time && p.Id.CompareTo(after.Id) < 0));
            }

            items = await query.OrderByDescending(p => p.UploadedAt).ThenByDescending(p => p.Id).Take(size + 1).ToListAsync(ct);
        }

        string? next = null;
        if (items.Count > size)
        {
            items.RemoveAt(size);
            var last = items[^1];
            next = new GalleryCursor(byTaken ? last.SortDate : last.UploadedAt, last.Id).Encode();
        }

        return TypedResults.Ok(new PhotoPage([.. items.Select(PhotoQueries.ToSummary)], next));
    }

    /// <summary>
    /// How many photos each month has (taken, else uploaded; in the viewer's calendar), newest first, for the
    /// gallery's year and month buttons. Follows the search, so only years with matching photos are offered.
    /// </summary>
    private static async Task<IResult> TimelineAsync(PhotoDbContext db, string? q, int? offset, CancellationToken ct)
    {
        if (PhotoQueries.ValidateSearch(q) is { } searchErrors)
        {
            return TypedResults.ValidationProblem(searchErrors);
        }

        if (!PhotoQueries.TryUtcOffset(offset, out var utcOffset))
        {
            return TypedResults.ValidationProblem(PhotoQueries.OffsetError());
        }

        var months = await PhotoQueries.Search(PhotoQueries.Library(db.Photos), q)
            .Select(p => p.SortDate + utcOffset)
            .GroupBy(local => new { local.Year, local.Month })
            .Select(g => new TimelineMonth(g.Key.Year, g.Key.Month, g.Count()))
            .ToListAsync(ct);
        return TypedResults.Ok(months.OrderByDescending(m => m.Year).ThenByDescending(m => m.Month).ToList());
    }

    /// <summary>
    /// Photos taken on the viewer's date (today, in their calendar) in earlier years, grouped by year, most recent
    /// year first. Only real capture dates count: an upload date says nothing about when a photo was taken.
    /// </summary>
    private static async Task<IResult> OnThisDayAsync(PhotoDbContext db, DateOnly? date, int? offset, CancellationToken ct)
    {
        if (date is not { } today)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["date"] = ["Give today's date (yyyy-MM-dd)."] });
        }

        if (!PhotoQueries.TryUtcOffset(offset, out var utcOffset))
        {
            return TypedResults.ValidationProblem(PhotoQueries.OffsetError());
        }

        var (year, month, day) = (today.Year, today.Month, today.Day);
        var photos = await PhotoQueries.Library(db.Photos)
            .Where(p => p.TakenAt != null)
            .Select(p => new { Photo = p, Local = p.TakenAt!.Value + utcOffset })
            .Where(x => x.Local.Month == month && x.Local.Day == day && x.Local.Year < year)
            .OrderByDescending(x => x.Local.Year)
            .ThenBy(x => x.Photo.TakenAt)
            .ThenBy(x => x.Photo.Id)
            .Take(MaxOnThisDayPhotos)
            .Select(x => new
            {
                x.Local.Year,
                Summary = new PhotoSummary(x.Photo.Id, x.Photo.FileName, x.Photo.Description, x.Photo.UploadedAt, x.Photo.TakenAt, x.Photo.Width, x.Photo.Height, x.Photo.RenditionState, x.Photo.RenditionVersion),
            })
            .ToListAsync(ct);

        return TypedResults.Ok(photos
            .GroupBy(p => p.Year)
            .Select(g => new OnThisDayYear(g.Key, year - g.Key, [.. g.Select(p => p.Summary)]))
            .ToList());
    }

    private static Dictionary<string, string[]>? ValidatePeriod(int? year, int? month)
    {
        if (year is < PhotoQueries.MinYear or > PhotoQueries.MaxYear)
        {
            return new() { ["year"] = ["That year is out of range."] };
        }

        if (month is null)
        {
            return null;
        }

        if (year is null)
        {
            return new() { ["month"] = ["Choose a year as well as a month."] };
        }

        return month is < 1 or > 12 ? new() { ["month"] = ["Months are numbered 1 to 12."] } : null;
    }

    // Only photos in the library are visible here. In-progress, failed and duplicate uploads are listed to their
    // uploader by /api/uploads; nobody can read or edit someone else's unfinished upload through this endpoint.
    private static async Task<IResult> GetAsync(Guid id, PhotoDbContext db, CancellationToken ct)
    {
        var photo = await db.Photos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Status == PhotoStatus.Ready, ct);
        return photo is null ? TypedResults.NotFound() : TypedResults.Ok(ToDetails(photo));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdatePhotoRequest request, PhotoDbContext db, AuditLog audit, CancellationToken ct)
    {
        if (request.Description is { Length: > MaxEditedDescriptionLength })
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["description"] = [$"Descriptions are limited to {MaxEditedDescriptionLength} characters."] });
        }

        var photo = await db.Photos.SingleOrDefaultAsync(p => p.Id == id && p.Status == PhotoStatus.Ready, ct);
        if (photo is null)
        {
            return TypedResults.NotFound();
        }

        var description = DescriptionSanitizer.Description(request.Description);
        photo.Description = description.Length == 0 ? null : description;
        photo.DescriptionSource = description.Length == 0 ? null : DescriptionSource.User;
        photo.DescriptionModel = null;
        photo.DescriptionState = description.Length == 0 ? ProcessingState.NotStarted : ProcessingState.Ready;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Changed meanwhile (another edit, or a worker finishing an AI description): do not overwrite blindly.
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "This photo changed while you were editing. Reload it and try again.");
        }

        await audit.WriteAsync("photo.describe.edit", id.ToString(), null);
        return TypedResults.Ok(ToDetails(photo));
    }

    /// <summary>
    /// Streams a rendition, decrypting it with the web key on the fly. If an archived photo has none yet, a
    /// worker is asked to create it (idempotent start) and the client is told to retry shortly.
    /// </summary>
    private static async Task<IResult> ServeRenditionAsync(
        Guid id,
        string blobName,
        HttpContext http,
        PhotoDbContext db,
        IBlobStore blobs,
        PhotoCryptoService crypto,
        PhotoWorkflowClient workflows,
        TimeProvider time,
        CancellationToken ct)
    {
        var photo = await db.Photos.AsNoTracking()
            .Where(p => p.Id == id && p.Status == PhotoStatus.Ready)
            .Select(p => new { p.RenditionState, p.RenditionVersion, p.ArchiveKey })
            .SingleOrDefaultAsync(ct);
        if (photo is null)
        {
            return TypedResults.NotFound();
        }

        var renditionState = photo.RenditionState;
        if (renditionState == ProcessingState.Ready)
        {
            try
            {
                // Renditions are small, so they are decrypted completely before responding: a corrupted or
                // tampered blob (or one encrypted with a retired web key) is detected while a clean error is
                // still possible, and is regenerated instead of failing after headers were sent.
                var content = await ReadRenditionAsync(blobName, blobs, crypto, ct);

                // Only cache immutably when ?v= matches the current RenditionVersion, so unversioned or stale
                // URLs (for example a poll started at ?v=0 before generation finished) are never pinned for a year.
                var versionMatched = photo.RenditionVersion > 0
                    && http.Request.Query.TryGetValue("v", out var versionValues)
                    && versionValues.Count == 1
                    && string.Equals(versionValues[0], photo.RenditionVersion.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
                http.Response.Headers.CacheControl = versionMatched
                    ? "private, max-age=31536000, immutable"
                    : "private, no-cache";
                return TypedResults.File(content, "image/webp");
            }
            catch (StorageObjectNotFoundException)
            {
                // The record says ready but the blob is gone (e.g. container cleaned up): reset to NotStarted so
                // a subsequent worker failure can record Failed instead of leaving the photo in an infinite loop.
                await ResetRenditionStateAsync(db, id, time, ct);
                renditionState = ProcessingState.NotStarted;
            }
            catch (Exception ex) when (ex is PhotoDecryptionException or PhotoKeyUnavailableException or CryptographicException or InvalidDataException)
            {
                CreateLogger(http).LogWarning(ex, "Rendition {BlobName} could not be decrypted; regenerating it", blobName);
                await ResetRenditionStateAsync(db, id, time, ct);
                renditionState = ProcessingState.NotStarted;
            }
        }

        if (renditionState is ProcessingState.Failed or ProcessingState.Unsupported || photo.ArchiveKey is null)
        {
            return TypedResults.NotFound();
        }

        if (renditionState != ProcessingState.Pending)
        {
            try
            {
                await workflows.StartEnsureRenditionsAsync(id);
            }
            catch (Exception ex) when (TemporalUnavailable.Matches(ex))
            {
                return TemporalUnavailable.Problem();
            }
        }

        http.Response.Headers.RetryAfter = "3";
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<byte[]> ReadRenditionAsync(string blobName, IBlobStore blobs, PhotoCryptoService crypto, CancellationToken ct)
    {
        var encrypted = await blobs.OpenReadAsync(BlobArea.Renditions, blobName, ct);
        await using var plaintext = await crypto.OpenWebDecryptStreamAsync(encrypted, ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await plaintext.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxRenditionBytes)
            {
                throw new InvalidDataException("The rendition is larger than any rendition this app writes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static Task<int> ResetRenditionStateAsync(PhotoDbContext db, Guid id, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.Photos.Where(p => p.Id == id && p.RenditionState == ProcessingState.Ready)
            .ExecuteUpdateAsync(
                s => s.SetProperty(p => p.RenditionState, ProcessingState.NotStarted)
                      .SetProperty(p => p.UpdatedAt, now),
                ct);
    }

    private static ILogger CreateLogger(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PhotoEndpoints));

    private static async Task<IResult> DescribeAsync(Guid id, PhotoDbContext db, PhotoWorkflowClient workflows, AuditLog audit, CancellationToken ct)
    {
        var previous = await db.Photos.AsNoTracking()
            .Where(p => p.Id == id && p.Status == PhotoStatus.Ready && p.ArchiveKey != null)
            .Select(p => (ProcessingState?)p.DescriptionState)
            .SingleOrDefaultAsync(ct);
        if (previous is null)
        {
            return TypedResults.NotFound();
        }

        await db.Photos.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.DescriptionState, ProcessingState.Pending), ct);
        try
        {
            await workflows.StartDescribeAsync(id);
        }
        catch (Exception ex) when (TemporalUnavailable.Matches(ex))
        {
            // Put the state back, or the viewer would show "Describing..." forever and keep the button disabled.
            await db.Photos.Where(p => p.Id == id && p.DescriptionState == ProcessingState.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.DescriptionState, previous.Value), CancellationToken.None);
            return TemporalUnavailable.Problem();
        }

        await audit.WriteAsync("photo.describe.request", id.ToString(), null);
        return TypedResults.Accepted((string?)null);
    }

    private static PhotoDetails ToDetails(Photo p) =>
        new(p.Id, p.FileName, p.OriginalFileName, p.Description, p.DescriptionSource, p.DescriptionState, p.Tags, p.ContentType, p.SizeBytes, p.Width,
            p.Height, p.TakenAt, p.UploadedAt, p.UploadedBy, p.Status, p.StatusDetail, p.RenditionState, p.RenditionVersion, p.EncryptionFormat, p.Source);
}
