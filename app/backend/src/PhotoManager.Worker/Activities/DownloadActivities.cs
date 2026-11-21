using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhotoManager.Core.Domain;
using PhotoManager.Data;
using PhotoManager.Data.Entities;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Storage;
using Temporalio.Activities;

namespace PhotoManager.Worker.Activities;

/// <summary>
/// Prepares downloads of originals. The worker decrypts originals with the archive key and streams them,
/// re-encrypted with the web key, into private blob storage; the API then streams them to the requester.
/// Nothing is buffered in full and plaintext is never written to storage.
/// </summary>
public sealed class DownloadActivities(
    PhotoDbContext db,
    PlaintextLoader loader,
    PhotoCryptoService crypto,
    IBlobStore blobs,
    IOptions<WorkerOptions> options,
    TimeProvider time,
    ILogger<DownloadActivities> logger)
{
    // Well inside the activity's 2-minute heartbeat timeout; the SDK throttles what it actually sends.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    /// <summary>Builds the download (a single original, or a ZIP of several) into the downloads container.</summary>
    /// <param name="downloadId">The download record.</param>
    /// <returns>A task that completes when the download is ready.</returns>
    [Activity("PrepareDownload")]
    public async Task PrepareDownloadAsync(Guid downloadId)
    {
        var context = ActivityExecutionContext.Current;
        var ct = context.CancellationToken;
        try
        {
            var download = await db.Downloads.SingleOrDefaultAsync(d => d.Id == downloadId, ct)
                ?? throw ActivityErrors.Permanent(ActivityErrors.NotFound, "The download request does not exist.");
            if (download.Status == DownloadStatus.Ready)
            {
                return;
            }

            if (download.PhotoIds.Count > options.Value.MaxPhotosPerDownload)
            {
                throw ActivityErrors.Permanent(ActivityErrors.TooLarge, $"A download may contain at most {options.Value.MaxPhotosPerDownload} photos.");
            }

            // Photos that stopped being available since they were selected (missing from the archive, hidden as
            // duplicates) are skipped; the requester is told how many.
            var photos = await db.Photos.AsNoTracking()
                .Where(p => download.PhotoIds.Contains(p.Id) && p.Status == PhotoStatus.Ready && p.ArchiveKey != null)
                .OrderBy(p => p.UploadedAt)
                .ToListAsync(ct);
            if (photos.Count == 0)
            {
                throw ActivityErrors.Permanent(ActivityErrors.NotFound, "None of the requested photos are available.");
            }

            var maxBytes = options.Value.MaxDownloadBytes;
            EnsureEstimatedSizeWithinLimit(photos.Select(p => p.SizeBytes ?? p.ArchiveSizeBytes ?? 0L), maxBytes);
            await EnsureWithinDailyLimitAsync(download, photos.Count, ct);

            var blobName = BlobNames.Download(download.Id);

            // What was requested decides the shape: asking for several photos always yields a ZIP.
            var single = download.PhotoIds.Count == 1;
            var budget = new DownloadByteBudget(maxBytes);
            long plaintextLength;
            var completed = false;
            try
            {
                var blobStream = await blobs.OpenWriteAsync(BlobArea.Downloads, blobName, ct);
                var encryptor = await crypto.CreateWebEncryptStreamAsync(blobStream, leaveOpen: false, ct);
                await using (encryptor)
                {
                    if (single)
                    {
                        var plaintext = await loader.OpenArchivedAsync(photos[0], ct);
                        await using (plaintext)
                        {
                            await CopyWithHeartbeatsAsync(plaintext, encryptor, budget, () => context.Heartbeat(), ct);
                        }
                    }
                    else
                    {
                        await WriteZipAsync(photos, encryptor, budget, context, ct);
                    }

                    await encryptor.CompleteAsync(ct);
                    plaintextLength = encryptor.PlaintextLength;
                    completed = true;
                }
            }
            catch
            {
                if (!completed)
                {
#pragma warning disable CA1031 // Best-effort cleanup of a partial blob must never mask the root download error.
                    try
                    {
                        await blobs.DeleteIfExistsAsync(BlobArea.Downloads, blobName, CancellationToken.None);
                    }
                    catch
                    {
                        // The downloads container's 1-day lifecycle rule catches anything left behind.
                    }
#pragma warning restore CA1031
                }

                throw;
            }

            var now = time.GetUtcNow();
            download.Status = DownloadStatus.Ready;
            download.BlobName = blobName;
            download.SizeBytes = plaintextLength;
            // A ZIP keeps the name it was requested with (the album's, or a dated "photos-..." name); the web tier
            // sanitizes it again when it serves the file.
            download.FileName = single ? photos[0].OriginalFileName : download.FileName;
            download.ContentType = single ? photos[0].ContentType : "application/zip";
            download.SkippedCount = download.PhotoIds.Count - photos.Count;
            download.ExpiresAt = now + options.Value.DownloadLifetime;
            download.PreparedAt = now;
            await db.SaveChangesAsync(ct);

            // Downloads are where originals leave the archive, so each one is logged by the worker itself, whatever
            // the web tier did or did not record in the audit trail.
            logger.LogInformation(
                "Prepared download {DownloadId} for user {RequestedById}: {PhotoCount} photos, {PlaintextBytes} bytes",
                download.Id,
                download.RequestedById,
                photos.Count,
                plaintextLength);
        }
        catch (Exception ex) when (ActivityErrors.TryTranslate(ex, out var failure))
        {
            throw failure;
        }
    }

    /// <summary>
    /// Enforces <see cref="WorkerOptions.MaxDownloadPhotosPerDay"/> across all users. It counts what the workers
    /// actually prepared (<see cref="PreparedDownload.PreparedAt"/> is written by workers only; the web tier's
    /// database role cannot change it), so not even a compromised web tier can raise the rate at which originals
    /// are decrypted for it. Concurrent downloads may overshoot the limit by at most one download each.
    /// </summary>
    private async Task EnsureWithinDailyLimitAsync(PreparedDownload download, int photoCount, CancellationToken cancellationToken)
    {
        var limit = options.Value.MaxDownloadPhotosPerDay;
        var since = time.GetUtcNow().AddDays(-1);
        var preparedLastDay = await db.Downloads.AsNoTracking()
            .Where(d => d.PreparedAt != null && d.PreparedAt > since)
            .SumAsync(d => d.PhotoIds.Count - d.SkippedCount, cancellationToken);
        if (preparedLastDay + photoCount > limit)
        {
            logger.LogWarning(
                "Download {DownloadId} for user {RequestedById} refused: {PreparedLastDay} photos were prepared in the last 24 hours and {PhotoCount} more would exceed the limit of {Limit}",
                download.Id,
                download.RequestedById,
                preparedLastDay,
                photoCount,
                limit);
            throw ActivityErrors.Permanent(
                ActivityErrors.LimitReached,
                string.Create(CultureInfo.InvariantCulture, $"The daily download limit of {limit:N0} photos (shared by everyone) has been reached. Try again tomorrow, or select fewer photos."));
        }
    }

    /// <summary>Marks a download as failed with a user-safe message.</summary>
    /// <param name="input">Download id and message.</param>
    /// <returns>A task that completes when the record is updated.</returns>
    [Activity("MarkDownloadFailed")]
    public async Task MarkDownloadFailedAsync(DownloadFailedInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var now = time.GetUtcNow();
        await db.Downloads.Where(d => d.Id == input.DownloadId && d.Status == DownloadStatus.Pending).ExecuteUpdateAsync(
            s => s.SetProperty(d => d.Status, DownloadStatus.Failed)
                  .SetProperty(d => d.Error, input.Error)
                  .SetProperty(d => d.UpdatedAt, now),
            ActivityExecutionContext.Current.CancellationToken);
    }

    private async Task WriteZipAsync(
        List<Photo> photos,
        Stream destination,
        DownloadByteBudget budget,
        ActivityExecutionContext context,
        CancellationToken cancellationToken)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var zip = await ZipArchive.CreateAsync(destination, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        await using (zip)
        {
            for (var i = 0; i < photos.Count; i++)
            {
                var photo = photos[i];

                // Photos are already compressed; storing avoids burning CPU for no gain.
                var entry = zip.CreateEntry(UniqueName(photo.OriginalFileName, usedNames), CompressionLevel.NoCompression);
                entry.LastWriteTime = ZipTimestamp(photo.TakenAt ?? photo.UploadedAt);
                var entryStream = await entry.OpenAsync(cancellationToken);
                await using (entryStream)
                {
                    var plaintext = await loader.OpenArchivedAsync(photo, cancellationToken);
                    await using (plaintext)
                    {
                        await CopyWithHeartbeatsAsync(plaintext, entryStream, budget, () => context.Heartbeat(), cancellationToken);
                    }
                }

                // Heartbeats let Temporal detect a stuck or crashed worker within the heartbeat timeout
                // instead of waiting out the (long) start-to-close timeout of a large ZIP.
                context.Heartbeat(i + 1);
            }
        }
    }

    /// <summary>
    /// Rejects downloads whose known photo sizes already exceed <paramref name="maxBytes"/> before opening any
    /// archive streams. Streaming copies still enforce the exact byte budget in case sizes were missing or stale.
    /// </summary>
    internal static void EnsureEstimatedSizeWithinLimit(IEnumerable<long> sizes, long maxBytes)
    {
        long total = 0;
        foreach (var raw in sizes)
        {
            var size = Math.Max(0L, raw);
            if (size > maxBytes - total)
            {
                throw ActivityErrors.Permanent(ActivityErrors.TooLarge, $"The download exceeds the {FormatByteLimit(maxBytes)} size limit.");
            }

            total += size;
        }
    }

    /// <summary>
    /// ZIP (DOS) timestamps only cover 1980 to 2107 and .NET throws outside that range; scanned family photos are
    /// often dated earlier, so the entry date is clamped instead of failing the whole download.
    /// </summary>
    /// <param name="value">The photo's date.</param>
    /// <returns>A date the ZIP format can store.</returns>
    internal static DateTimeOffset ZipTimestamp(DateTimeOffset value) => value.Year switch
    {
        < 1980 => new DateTimeOffset(1980, 1, 1, 0, 0, 0, value.Offset),
        > 2107 => new DateTimeOffset(2107, 12, 31, 0, 0, 0, value.Offset),
        _ => value,
    };

    // Copies while heartbeating and enforcing the cumulative download byte budget across all entries.
    internal static async Task CopyWithHeartbeatsAsync(
        Stream source,
        Stream destination,
        DownloadByteBudget budget,
        Action heartbeat,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            var lastHeartbeat = Stopwatch.GetTimestamp();
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                budget.Consume(read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                if (Stopwatch.GetElapsedTime(lastHeartbeat) >= HeartbeatInterval)
                {
                    heartbeat();
                    lastHeartbeat = Stopwatch.GetTimestamp();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static string FormatByteLimit(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):0.#} GB"),
        >= 1024L * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };

    /// <summary>Tracks cumulative plaintext bytes across every photo in one prepared download.</summary>
    internal sealed class DownloadByteBudget(long limit)
    {
        private long _used;

        public void Consume(int count)
        {
            if (count > limit - _used)
            {
                throw ActivityErrors.Permanent(ActivityErrors.TooLarge, $"The download exceeds the {FormatByteLimit(limit)} size limit.");
            }

            _used += count;
        }
    }

    private static string UniqueName(string fileName, HashSet<string> used)
    {
        // Names were sanitized on upload/index (no path separators), so ZIP entries cannot escape the
        // extraction directory ("zip slip") on the user's machine.
        var candidate = FileNames.Sanitize(fileName);
        var stem = Path.GetFileNameWithoutExtension(candidate);
        var extension = Path.GetExtension(candidate);
        for (var n = 2; !used.Add(candidate); n++)
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({n}){extension}");
        }

        return candidate;
    }
}
