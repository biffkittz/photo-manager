using Microsoft.EntityFrameworkCore;
using PhotoManager.Api.Audit;
using PhotoManager.Api.Auth;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Photos;
using PhotoManager.Core.Domain;
using PhotoManager.Data;
using PhotoManager.Data.Entities;

namespace PhotoManager.Api.Albums;

/// <summary>
/// Albums. They are shared by the whole family: everyone signed in sees every album and can create, rename and delete
/// albums, choose covers, and add or remove photos (docs/adr/0013-shared-albums-and-timeline.md). Nothing is deleted: deleting an
/// album or removing a photo from one only records when it happened, and photos stay in the library either way.
/// </summary>
public static class AlbumEndpoints
{
    /// <summary>Most photos one request may add or remove (the most a gallery selection or a download holds).</summary>
    public const int MaxPhotosPerRequest = 500;

    private const int DefaultPageSize = 60;
    private const int MaxPageSize = 120;

    /// <summary>Maps <c>/api/albums/*</c>.</summary>
    /// <param name="api">The /api route group.</param>
    /// <returns>The same group.</returns>
    public static RouteGroupBuilder MapAlbumEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var albums = api.MapGroup("/albums");
        albums.MapGet("/", ListAsync);
        albums.MapPost("/", CreateAsync);
        albums.MapGet("/{id:guid}", GetAsync);
        albums.MapPatch("/{id:guid}", UpdateAsync);
        albums.MapDelete("/{id:guid}", DeleteAsync);
        albums.MapGet("/{id:guid}/photos", PhotosAsync);
        albums.MapPost("/{id:guid}/photos", AddPhotosAsync);
        albums.MapPost("/{id:guid}/photos/remove", RemovePhotosAsync);
        return api;
    }

    /// <summary>
    /// Albums with their photo count, date range and cover: the chosen cover while it is still in the album, else the
    /// first photo. Only library photos count.
    /// </summary>
    /// <param name="albums">The albums to summarize.</param>
    /// <returns>The summaries, in the order of <paramref name="albums"/>.</returns>
    internal static IQueryable<AlbumSummary> Summaries(IQueryable<Album> albums) => albums
        .Select(a => new
        {
            a.Id,
            a.Name,
            a.CoverPhotoId,
            Photos = a.Photos.Where(ap => ap.RemovedAt == null && ap.Photo.Status == PhotoStatus.Ready).Select(ap => ap.Photo),
        })
        .Select(a => new AlbumSummary(
            a.Id,
            a.Name,
            a.Photos.Count(),
            a.Photos.Min(p => (DateTimeOffset?)p.SortDate),
            a.Photos.Max(p => (DateTimeOffset?)p.SortDate),
            a.Photos
                .OrderBy(p => p.Id == a.CoverPhotoId ? 0 : 1)
                .ThenBy(p => p.SortDate)
                .ThenBy(p => p.Id)
                .Select(p => new PhotoSummary(p.Id, p.OriginalFileName, p.Description, p.UploadedAt, p.TakenAt, p.Width, p.Height, p.RenditionState, p.RenditionVersion))
                .FirstOrDefault()));

    // Most recently changed first: the album someone is putting together stays at the top.
    private static async Task<IResult> ListAsync(PhotoDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await Summaries(Existing(db).OrderByDescending(a => a.UpdatedAt).ThenByDescending(a => a.Id)).ToListAsync(ct));

    private static async Task<IResult> GetAsync(Guid id, PhotoDbContext db, CancellationToken ct) =>
        await SummaryAsync(db, id, ct) is { } album ? TypedResults.Ok(album) : TypedResults.NotFound();

    private static async Task<IResult> CreateAsync(CreateAlbumRequest request, HttpContext http, PhotoDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var name = ValidName(request.Name, errors);
        var photoIds = request.PhotoIds ?? [];
        if (photoIds.Count > MaxPhotosPerRequest)
        {
            errors["photoIds"] = [$"Select at most {MaxPhotosPerRequest} photos."];
        }

        if (name is null || errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var album = new Album { Id = Guid.CreateVersion7(), Name = name, CreatedById = http.User.UserId() };
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            db.Albums.Add(album);
            await db.SaveChangesAsync(ct);
            if (photoIds.Count > 0)
            {
                await AddAsync(db, album.Id, photoIds, time.GetUtcNow(), ct);
            }

            await transaction.CommitAsync(ct);
        }

        await audit.WriteAsync("album.create", album.Id.ToString(), new { name, count = photoIds.Count });
        return TypedResults.Created($"/api/albums/{album.Id}", await SummaryAsync(db, album.Id, ct));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateAlbumRequest request, PhotoDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name is null ? null : ValidName(request.Name, errors);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!await Existing(db).AnyAsync(a => a.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        if (request.CoverPhotoId is { } coverId && !await InAlbum(db, id).AnyAsync(ap => ap.PhotoId == coverId, ct))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["coverPhotoId"] = ["Choose a photo in this album as its cover."] });
        }

        var now = time.GetUtcNow();
        await Existing(db).Where(a => a.Id == id).ExecuteUpdateAsync(
            s =>
            {
                s.SetProperty(a => a.UpdatedAt, now);
                if (name is not null)
                {
                    s.SetProperty(a => a.Name, name);
                }

                if (request.CoverPhotoId is { } cover)
                {
                    s.SetProperty(a => a.CoverPhotoId, cover);
                }
            },
            ct);

        await audit.WriteAsync("album.update", id.ToString(), new { name, cover = request.CoverPhotoId });
        return await SummaryAsync(db, id, ct) is { } album ? TypedResults.Ok(album) : TypedResults.NotFound();
    }

    private static async Task<IResult> DeleteAsync(Guid id, PhotoDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var deleted = await Existing(db).Where(a => a.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(a => a.DeletedAt, now).SetProperty(a => a.UpdatedAt, now),
            ct);
        if (deleted == 0)
        {
            return TypedResults.NotFound();
        }

        await audit.WriteAsync("album.delete", id.ToString(), null);
        return TypedResults.NoContent();
    }

    // Oldest first, so an album reads like the event it holds; keyset pagination as in the gallery.
    private static async Task<IResult> PhotosAsync(Guid id, PhotoDbContext db, string? cursor, int? limit, CancellationToken ct)
    {
        GalleryCursor? after = null;
        if (cursor is not null && !GalleryCursor.TryParse(cursor, out after))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["cursor"] = ["The cursor is invalid."] });
        }

        if (!await Existing(db).AnyAsync(a => a.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var query = InAlbum(db, id).Select(ap => ap.Photo);
        if (after is not null)
        {
            query = query.Where(p => p.SortDate > after.Time || (p.SortDate == after.Time && p.Id.CompareTo(after.Id) > 0));
        }

        var items = await query.OrderBy(p => p.SortDate).ThenBy(p => p.Id).Take(size + 1).Select(PhotoQueries.Summary).ToListAsync(ct);
        string? next = null;
        if (items.Count > size)
        {
            items.RemoveAt(size);
            var last = items[^1];
            next = new GalleryCursor(last.TakenAt ?? last.UploadedAt, last.Id).Encode();
        }

        return TypedResults.Ok(new PhotoPage(items, next));
    }

    private static async Task<IResult> AddPhotosAsync(Guid id, AlbumPhotosRequest request, PhotoDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (ValidPhotoIds(request.PhotoIds) is not { } photoIds)
        {
            return PhotoIdsProblem();
        }

        if (!await Existing(db).AnyAsync(a => a.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        await AddAsync(db, id, photoIds, now, ct);
        await db.Albums.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.UpdatedAt, now), ct);
        await audit.WriteAsync("album.add", id.ToString(), new { count = photoIds.Length });
        return await SummaryAsync(db, id, ct) is { } album ? TypedResults.Ok(album) : TypedResults.NotFound();
    }

    private static async Task<IResult> RemovePhotosAsync(Guid id, AlbumPhotosRequest request, PhotoDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (ValidPhotoIds(request.PhotoIds) is not { } photoIds)
        {
            return PhotoIdsProblem();
        }

        if (!await Existing(db).AnyAsync(a => a.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        var now = time.GetUtcNow();
        await db.AlbumPhotos
            .Where(ap => ap.AlbumId == id && photoIds.Contains(ap.PhotoId) && ap.RemovedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(ap => ap.RemovedAt, now), ct);

        // A removed cover is forgotten, so the photo does not become the cover again if it is ever added back.
        await db.Albums.Where(a => a.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(a => a.UpdatedAt, now)
                  .SetProperty(a => a.CoverPhotoId, a => a.CoverPhotoId != null && photoIds.Contains(a.CoverPhotoId.Value) ? null : a.CoverPhotoId),
            ct);
        await audit.WriteAsync("album.remove", id.ToString(), new { count = photoIds.Length });
        return await SummaryAsync(db, id, ct) is { } album ? TypedResults.Ok(album) : TypedResults.NotFound();
    }

    private static IQueryable<Album> Existing(PhotoDbContext db) => db.Albums.AsNoTracking().Where(a => a.DeletedAt == null);

    private static IQueryable<AlbumPhoto> InAlbum(PhotoDbContext db, Guid id) =>
        db.AlbumPhotos.AsNoTracking().Where(ap => ap.AlbumId == id && ap.RemovedAt == null && ap.Photo.Status == PhotoStatus.Ready);

    private static Task<AlbumSummary?> SummaryAsync(PhotoDbContext db, Guid id, CancellationToken ct) =>
        Summaries(Existing(db).Where(a => a.Id == id)).SingleOrDefaultAsync(ct);

    /// <summary>
    /// Puts library photos in the album, or back in it if they were removed; photos already in it stay as they are,
    /// and ids of anything that is not in the library are ignored. One statement, so concurrent adds cannot collide.
    /// </summary>
    private static Task<int> AddAsync(PhotoDbContext db, Guid albumId, IEnumerable<Guid> photoIds, DateTimeOffset now, CancellationToken ct)
    {
        var ids = photoIds.Distinct().ToArray();
        var ready = nameof(PhotoStatus.Ready);
        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO album_photos (album_id, photo_id, added_at)
            SELECT {albumId}, p.id, {now} FROM photos AS p WHERE p.id = ANY({ids}) AND p.status = {ready}
            ON CONFLICT (album_id, photo_id) DO UPDATE SET added_at = EXCLUDED.added_at, removed_at = NULL
            WHERE album_photos.removed_at IS NOT NULL
            """,
            ct);
    }

    private static string? ValidName(string? value, Dictionary<string, string[]> errors)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is > 0 and <= Album.MaxNameLength && !name.Any(char.IsControl))
        {
            return name;
        }

        errors["name"] = [$"Enter an album name of 1 to {Album.MaxNameLength} characters."];
        return null;
    }

    // The raw count is checked before removing duplicates, so a payload of repeated ids cannot force a large set.
    private static Guid[]? ValidPhotoIds(IReadOnlyList<Guid>? photoIds) =>
        photoIds is { Count: > 0 and <= MaxPhotosPerRequest } ? [.. photoIds.Distinct()] : null;

    private static Microsoft.AspNetCore.Http.HttpResults.ValidationProblem PhotoIdsProblem() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["photoIds"] = [$"Select between 1 and {MaxPhotosPerRequest} photos."] });
}
