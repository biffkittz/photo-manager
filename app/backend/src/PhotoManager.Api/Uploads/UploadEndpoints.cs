using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using PhotoManager.Api.Auth;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Security;
using PhotoManager.Data;

namespace PhotoManager.Api.Uploads;

/// <summary>Upload endpoints: one file per request so the browser can parallelize and report progress per file.</summary>
public static class UploadEndpoints
{
    private const int MaxBoundaryLength = 70;

    /// <summary>Maps <c>/api/uploads/*</c>.</summary>
    /// <param name="api">The /api route group.</param>
    /// <returns>The same group.</returns>
    public static RouteGroupBuilder MapUploadEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var uploads = api.MapGroup("/uploads");
        uploads.MapPost("/", UploadAsync)
            .RequireRateLimiting(RateLimitPolicies.Upload)
            .WithMetadata(new RequestSizeLimitAttribute(120L * 1024 * 1024));
        uploads.MapGet("/", ListAsync);
        uploads.MapPost("/{id:guid}/retry", (Guid id, HttpContext http, UploadService service, CancellationToken ct) =>
            service.RetryAsync(id, http.User.GitHubId(), ct))
            .RequireRateLimiting(RateLimitPolicies.Upload);
        return api;
    }

    /// <summary>
    /// Reads the multipart body manually (no IFormFile) so the file streams straight into encryption without
    /// being buffered to a temp file. CSRF is enforced by the /api group's antiforgery filter.
    /// </summary>
    private static async Task<IResult> UploadAsync(HttpContext http, UploadService service, Guid? batchId, CancellationToken ct)
    {
        if (!MediaTypeHeaderValue.TryParse(http.Request.ContentType, out var mediaType)
            || !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Expected multipart/form-data.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > MaxBoundaryLength)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid multipart boundary.");
        }

        // No per-section BodyLengthLimit: the file size is enforced by UploadService (which answers 413), and the
        // whole request is capped by the endpoint's RequestSizeLimit. A reader limit would throw mid-read and
        // surface as a 500 instead.
        var reader = new MultipartReader(boundary, http.Request.Body)
        {
            HeadersCountLimit = 8,
            HeadersLengthLimit = 8 * 1024,
        };

        try
        {
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
            {
                if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                    && disposition.IsFileDisposition()
                    && string.Equals(disposition.Name.Value, "file", StringComparison.Ordinal))
                {
                    var fileName = disposition.FileNameStar.HasValue ? disposition.FileNameStar.Value : disposition.FileName.Value;
                    var outcome = await service.StageAsync(section.Body, fileName, batchId, http.User.Login(), http.User.GitHubId(), ct);
                    return outcome.Problem ?? TypedResults.Accepted((string?)null, outcome.Accepted);
                }
            }
        }
        catch (InvalidDataException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The multipart request is malformed.");
        }

        return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "No file part named 'file' was found.");
    }

    private static async Task<IResult> ListAsync(HttpContext http, PhotoDbContext db, Guid? batchId, CancellationToken ct)
    {
        var userId = http.User.GitHubId();
        var query = db.Photos.AsNoTracking().Where(p => p.UploadedById == userId && p.Source == Core.Domain.PhotoSource.Upload);
        query = batchId is { } batch
            ? query.Where(p => p.UploadBatchId == batch)
            : query.OrderByDescending(p => p.UploadedAt).Take(100);
        var items = await query
            .OrderByDescending(p => p.UploadedAt)
            .Take(1000)
            .Select(p => new UploadItemStatus(p.Id, p.OriginalFileName, p.Status, p.StatusDetail, p.Description, p.DescriptionState, p.RenditionState, p.RenditionVersion, p.UploadedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(items);
    }
}
