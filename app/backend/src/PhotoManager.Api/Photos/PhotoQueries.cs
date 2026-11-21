using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PhotoManager.Api.Contracts;
using PhotoManager.Core.Domain;
using PhotoManager.Data.Entities;

namespace PhotoManager.Api.Photos;

/// <summary>
/// Query pieces shared by the gallery, its timeline and the albums: the search filter, the viewer's calendar and
/// the tile projection. Only library (<see cref="PhotoStatus.Ready"/>) photos are ever listed.
/// </summary>
internal static class PhotoQueries
{
    /// <summary>Longest search text accepted.</summary>
    public const int MaxQueryLength = 200;

    /// <summary>Widest UTC offset in use anywhere (UTC+14:00); offsets are in minutes east of UTC.</summary>
    public const int MaxUtcOffsetMinutes = 14 * 60;

    /// <summary>Earliest and latest years the gallery can be filtered by.</summary>
    public const int MinYear = 1;

    /// <inheritdoc cref="MinYear"/>
    public const int MaxYear = 9998;

    /// <summary>Projects a photo to a gallery tile inside a query.</summary>
    public static readonly Expression<Func<Photo, PhotoSummary>> Summary = p =>
        new PhotoSummary(p.Id, p.FileName, p.Description, p.UploadedAt, p.TakenAt, p.Width, p.Height, p.RenditionState, p.RenditionVersion);

    private static readonly Func<Photo, PhotoSummary> CompiledSummary = Summary.Compile();

    /// <summary>The library: photos everyone can see.</summary>
    /// <param name="photos">The photos table.</param>
    /// <returns>Ready photos, untracked.</returns>
    public static IQueryable<Photo> Library(DbSet<Photo> photos) => photos.AsNoTracking().Where(p => p.Status == PhotoStatus.Ready);

    /// <summary>Maps a loaded photo to a gallery tile.</summary>
    /// <param name="photo">The photo.</param>
    /// <returns>The tile.</returns>
    public static PhotoSummary ToSummary(Photo photo) => CompiledSummary(photo);

    /// <summary>Checks search text; returns the validation errors, or null when it is fine.</summary>
    /// <param name="q">The search text.</param>
    /// <returns>Errors keyed by parameter, or null.</returns>
    public static Dictionary<string, string[]>? ValidateSearch(string? q)
    {
        if (q is { Length: > MaxQueryLength })
        {
            return new() { ["q"] = [$"Search text is limited to {MaxQueryLength} characters."] };
        }

        // PostgreSQL rejects NUL in text (error 22021); refuse control characters up front instead of failing with a 500.
        return q is not null && q.Any(char.IsControl)
            ? new() { ["q"] = ["Search text contains invalid characters."] }
            : null;
    }

    /// <summary>
    /// Full-text search over description, file names and tags, plus a plain match on either file name (the one shown,
    /// and the one the photo arrived with).
    /// </summary>
    /// <param name="query">Photos to filter.</param>
    /// <param name="q">Search text (validated by <see cref="ValidateSearch"/>); blank means everything.</param>
    /// <returns>The matching photos.</returns>
    public static IQueryable<Photo> Search(IQueryable<Photo> query, string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return query;
        }

        var text = q.Trim();
        var pattern = $"%{EscapeLike(text)}%";
        return query.Where(p =>
            p.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", text))
            || EF.Functions.ILike(p.FileName, pattern, "\\")
            || EF.Functions.ILike(p.OriginalFileName, pattern, "\\"));
    }

    /// <summary>
    /// Reads the viewer's UTC offset (minutes east of UTC, as the browser reports it). Dates are grouped by the
    /// viewer's calendar so that the timeline and "On this day" agree with the dates shown on the tiles.
    /// </summary>
    /// <param name="minutes">The offset; null means UTC.</param>
    /// <param name="offset">The offset as a time span.</param>
    /// <returns>False when the offset is out of range.</returns>
    public static bool TryUtcOffset(int? minutes, out TimeSpan offset)
    {
        offset = TimeSpan.FromMinutes(minutes ?? 0);
        return minutes is null or (>= -MaxUtcOffsetMinutes and <= MaxUtcOffsetMinutes);
    }

    /// <summary>The validation error for an offset outside <see cref="TryUtcOffset"/>'s range.</summary>
    public static Dictionary<string, string[]> OffsetError() =>
        new() { ["offset"] = [$"The UTC offset must be between -{MaxUtcOffsetMinutes} and {MaxUtcOffsetMinutes} minutes."] };

    /// <summary>The instants a year, or one month of it, starts and ends at in the viewer's calendar.</summary>
    /// <param name="year">Year (<see cref="MinYear"/> to <see cref="MaxYear"/>).</param>
    /// <param name="month">Month (1 to 12), or null for the whole year.</param>
    /// <param name="offset">The viewer's UTC offset.</param>
    /// <returns>Start (inclusive) and end (exclusive), in UTC.</returns>
    public static (DateTimeOffset From, DateTimeOffset To) Period(int year, int? month, TimeSpan offset)
    {
        var start = new DateTime(year, month ?? 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var end = month is null ? start.AddYears(1) : start.AddMonths(1);
        return (ToUtc(start, offset), ToUtc(end, offset));
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeSpan offset) =>
        new(Math.Clamp(local.Ticks - offset.Ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), TimeSpan.Zero);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
