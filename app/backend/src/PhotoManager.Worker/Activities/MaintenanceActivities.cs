using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PhotoManager.Core.Crypto;
using PhotoManager.Core.Domain;
using PhotoManager.Core.Workflows;
using PhotoManager.Data;
using PhotoManager.Data.Entities;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Storage;
using Temporalio.Activities;

namespace PhotoManager.Worker.Activities;

/// <summary>Re-indexing, job bookkeeping and backfill paging (task queue photo-batch).</summary>
public sealed class MaintenanceActivities(
    PhotoDbContext db,
    IArchiveStore archive,
    PhotoCryptoService crypto,
    TimeProvider time,
    ILogger<MaintenanceActivities> logger)
{
    private const int ListPageSize = 1000;
    private const string DuplicateDetail = "This photo is already in the library.";

    /// <summary>Creates the job record if missing (scheduled runs) and marks it running.</summary>
    /// <param name="input">Deterministic job id, kind and workflow id.</param>
    /// <returns>The job id.</returns>
    [Activity("EnsureJob")]
    public async Task<Guid> EnsureJobAsync(EnsureJobInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var now = time.GetUtcNow();
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == input.JobId, ct);
        if (job is null)
        {
            db.Jobs.Add(new MaintenanceJob
            {
                Id = input.JobId,
                Kind = input.Kind,
                Status = JobStatus.Running,
                WorkflowId = input.WorkflowId,
                RunId = input.RunId,
                RequestedBy = "schedule",
                StartedAt = now,
            });
        }
        else if (job.Status is JobStatus.Cancelled or JobStatus.Failed or JobStatus.Completed)
        {
            // Closed before this run got going: stopped while its start was still being recorded, or recorded as
            // "not started" because Temporal looked unavailable when it had in fact accepted the start. The run must
            // not do the work anyway, and the record keeps saying what the user was told.
            throw ActivityErrors.Permanent(ActivityErrors.InvalidState, "The job was closed before it started.");
        }
        else if (job.Status == JobStatus.Queued)
        {
            job.Status = JobStatus.Running;
            job.StartedAt = now;
            job.WorkflowId = input.WorkflowId;
            job.RunId ??= input.RunId;
        }

        await db.SaveChangesAsync(ct);
        return input.JobId;
    }

    /// <summary>Records job progress (and the final outcome when the status is terminal).</summary>
    /// <param name="input">Progress values.</param>
    /// <returns>A task that completes when the record is updated.</returns>
    [Activity("RecordJobProgress")]
    public async Task RecordJobProgressAsync(JobProgressInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var now = time.GetUtcNow();
        DateTimeOffset? completedAt = input.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled ? now : null;
        var summary = input.Summary is { Length: > 1000 } long_ ? long_[..1000] : input.Summary;
        await db.Jobs.Where(j => j.Id == input.JobId).ExecuteUpdateAsync(
            s => s.SetProperty(j => j.Status, input.Status)
                  .SetProperty(j => j.ProcessedCount, input.Processed)
                  .SetProperty(j => j.SucceededCount, input.Succeeded)
                  .SetProperty(j => j.FailedCount, input.Failed)
                  .SetProperty(j => j.Summary, summary)
                  .SetProperty(j => j.CompletedAt, completedAt)
                  .SetProperty(j => j.UpdatedAt, now),
            ActivityExecutionContext.Current.CancellationToken);
    }

    /// <summary>
    /// Indexes one page (up to 1000 objects) of the archive: creates records for new objects, refreshes
    /// changed ones, and stamps <see cref="Photo.LastSeenAt"/> on everything it sees.
    /// </summary>
    /// <remarks>
    /// Rows are read without tracking and changed with targeted updates, so editing a photo of this page meanwhile
    /// (a description, a finished rendition) cannot fail the page with a concurrency error. A photo that comes back
    /// as Ready (restored from Missing, or recovered from its v2 file after losing the database) but whose content
    /// is already Ready under another record becomes Duplicate, as ingest would have recorded it: two identical
    /// uploads archived at the same time leave two v2 files with the same hash, and without this a disaster
    /// recovery would fail on <see cref="DbErrors.UniqueReadyContentIndex"/> at that page and every page after it.
    /// </remarks>
    /// <param name="continuationToken">S3 continuation token, or null for the first page.</param>
    /// <returns>The next token and this page's counters.</returns>
    [Activity("IndexArchivePage")]
    public async Task<IndexPageResult> IndexArchivePageAsync(string? continuationToken)
    {
        var context = ActivityExecutionContext.Current;
        var ct = context.CancellationToken;
        try
        {
            var page = await archive.ListAsync(continuationToken, ListPageSize, ct);
            var objects = page.Objects.Where(o => ArchiveObjectKeys.TryClassify(o.Key, out _)).ToList();
            var keys = objects.Select(o => o.Key).ToList();
            var byKey = await db.Photos.AsNoTracking()
                .Where(p => p.ArchiveKey != null && keys.Contains(p.ArchiveKey))
                .Select(p => new IndexedPhoto(p.Id, p.ArchiveKey!, p.Status, p.ArchiveETag, p.ArchiveSizeBytes, p.Sha256))
                .ToDictionaryAsync(p => p.ArchiveKey, ct);

            // v2 objects carry the photo id in their key; a record may exist without an archive key yet
            // (ingest uploaded the object but has not completed), so those must not be inserted twice.
            var v2Ids = objects.Select(o => ArchiveObjectKeys.TryParseV2PhotoId(o.Key, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
            var existingV2Ids = await db.Photos.Where(p => v2Ids.Contains(p.Id)).Select(p => p.Id).ToHashSetAsync(ct);

            var now = time.GetUtcNow();
            var seen = new List<Guid>(objects.Count);
            var replaced = new List<(IndexedPhoto Photo, ArchiveObjectInfo Item)>();
            var restored = new List<IndexedPhoto>();
            var created = new List<Photo>();
            foreach (var item in objects)
            {
                if (byKey.TryGetValue(item.Key, out var existing))
                {
                    seen.Add(existing.Id);
                    if (existing.ArchiveETag != item.ETag || existing.ArchiveSizeBytes != item.Size)
                    {
                        replaced.Add((existing, item));
                    }
                    else if (existing.Status == PhotoStatus.Missing)
                    {
                        restored.Add(existing);
                    }

                    continue;
                }

                // Add, not Contains: two keys on one page carrying the same photo id must not both be inserted.
                if (ArchiveObjectKeys.TryParseV2PhotoId(item.Key, out var v2Id) && !existingV2Ids.Add(v2Id))
                {
                    continue;
                }

                created.Add(await CreateRecordAsync(item, now, ct));
                if (created.Count % 50 == 0)
                {
                    context.Heartbeat(created.Count);
                }
            }

            foreach (var (photo, item) in replaced)
            {
                await RefreshReplacedAsync(photo.Id, item, now, ct);
            }

            // At most one Ready photo per content hash. Hashes already Ready elsewhere win; within the page, the
            // first claim wins (restored records before newly created ones).
            var candidateHashes = restored.Select(p => p.Sha256).Concat(created.Select(p => p.Sha256))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var readyHashes = candidateHashes.Count == 0
                ? new HashSet<string>(StringComparer.Ordinal)
                : await db.Photos.AsNoTracking()
                    .Where(p => p.Status == PhotoStatus.Ready && p.Sha256 != null && candidateHashes.Contains(p.Sha256))
                    .Select(p => p.Sha256!)
                    .ToHashSetAsync(StringComparer.Ordinal, ct);

            foreach (var photo in restored)
            {
                await RestoreAsync(photo.Id, ClaimContent(readyHashes, photo.Sha256), now, ct);
            }

            foreach (var record in created.Where(r => r.Status == PhotoStatus.Ready && !ClaimContent(readyHashes, r.Sha256)))
            {
                record.Status = PhotoStatus.Duplicate;
                record.StatusDetail = DuplicateDetail;
            }

            if (created.Count > 0)
            {
                db.Photos.AddRange(created);
                await db.SaveChangesAsync(ct);
            }

            if (seen.Count > 0)
            {
                await db.Photos.Where(p => seen.Contains(p.Id)).ExecuteUpdateAsync(s => s.SetProperty(p => p.LastSeenAt, now), ct);
            }

            return new IndexPageResult(page.NextContinuationToken, new ReindexProgress(1, objects.Count, created.Count, replaced.Count + restored.Count));
        }
        catch (Exception ex) when (ActivityErrors.TryTranslate(ex, out var failure))
        {
            throw failure;
        }
    }

    /// <summary>
    /// Claims a content hash for a photo about to become Ready. False means another photo already holds it, so this
    /// one must be recorded as a duplicate. Photos without a known hash (legacy files not yet decoded) always may.
    /// </summary>
    /// <param name="readyHashes">Hashes already Ready; updated with the claimed one.</param>
    /// <param name="sha256">The photo's hash, if known.</param>
    /// <returns>Whether the photo may be Ready.</returns>
    internal static bool ClaimContent(ISet<string> readyHashes, string? sha256) => sha256 is null || readyHashes.Add(sha256);

    /// <summary>
    /// Marks photos that a completed scan did not see as missing. Refuses implausible sweeps unless forced: an
    /// empty listing, or one that would mark more than <see cref="SweepLimit"/> photos missing (for example after
    /// a credentials or endpoint misconfiguration), is reported instead of applied. Missing is reversible: a later
    /// scan that sees the object again restores the photo.
    /// </summary>
    /// <param name="input">Threshold, scan size and the force flag.</param>
    /// <returns>The number marked, or a warning.</returns>
    [Activity("SweepMissing")]
    public async Task<SweepResult> SweepMissingAsync(SweepInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var candidates = db.Photos.Where(p =>
            (p.Status == PhotoStatus.Ready || p.Status == PhotoStatus.Duplicate)
            && p.ArchiveKey != null
            && (p.LastSeenAt == null || p.LastSeenAt < input.SeenBefore));
        var count = await candidates.CountAsync(ct);
        if (count == 0)
        {
            return new SweepResult(0, null);
        }

        var total = await db.Photos.CountAsync(p => (p.Status == PhotoStatus.Ready || p.Status == PhotoStatus.Duplicate) && p.ArchiveKey != null, ct);
        var limit = SweepLimit(total);
        if (!input.Force && (input.ObjectsSeen == 0 || count > limit))
        {
            logger.LogWarning("Re-index sweep skipped: {Count} of {Total} photos would be marked missing (limit {Limit})", count, total, limit);
            return new SweepResult(0, string.Create(
                CultureInfo.InvariantCulture,
                $"{count} of {total} photos were not found in the archive, more than the safety limit of {limit}, so none were marked missing. Check bucket access; if those files really were removed, run Re-index again with \"Mark missing even if many\"."));
        }

        if (input.Force && count > limit)
        {
            logger.LogWarning("Forced re-index sweep: marking {Count} of {Total} photos missing (limit {Limit})", count, total, limit);
        }

        var now = time.GetUtcNow();
        var marked = await candidates.ExecuteUpdateAsync(
            s => s.SetProperty(p => p.Status, PhotoStatus.Missing)
                  .SetProperty(p => p.StatusDetail, "The archive object was not found during re-indexing.")
                  .SetProperty(p => p.UpdatedAt, now),
            ct);
        return new SweepResult(marked, null);
    }

    /// <summary>
    /// The most photos an unforced sweep may mark missing: 10% of the library, but at least 3 so that deleting a
    /// handful of files from a small library still works. (The previous fixed minimum of 25 allowed a broken
    /// listing to empty a small library entirely.)
    /// </summary>
    /// <param name="total">Photos currently in the library.</param>
    /// <returns>The limit.</returns>
    internal static int SweepLimit(int total) => Math.Max(3, total / 10);

    /// <summary>Returns the next batch of photos needing a backfill, in id order after the cursor.</summary>
    /// <param name="query">Kind, cursor and batch size.</param>
    /// <returns>Photo ids.</returns>
    [Activity("FindBackfillCandidates")]
    public async Task<IReadOnlyList<Guid>> FindBackfillCandidatesAsync(BackfillQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var photos = db.Photos.AsNoTracking().Where(p => p.Status == PhotoStatus.Ready && p.ArchiveKey != null);
        photos = query.Kind switch
        {
            JobKind.BackfillRenditions => photos.Where(p => p.RenditionState == ProcessingState.NotStarted || p.RenditionState == ProcessingState.Failed),
            JobKind.BackfillDescriptions => photos.Where(p => p.DescriptionState == ProcessingState.NotStarted || p.DescriptionState == ProcessingState.Failed),
            _ => throw ActivityErrors.Permanent(ActivityErrors.InvalidState, $"{query.Kind} is not a backfill."),
        };

        if (query.AfterPhotoId is { } after)
        {
            photos = photos.Where(p => p.Id.CompareTo(after) > 0);
        }

        return await photos.OrderBy(p => p.Id).Select(p => p.Id).Take(query.Limit).ToListAsync(ActivityExecutionContext.Current.CancellationToken);
    }

    // The object was replaced in place (e.g. re-uploaded by protect.ps1): renditions and content facts are stale, and
    // a photo hidden as a duplicate (or missing) may no longer be one. The next thumbnail backfill recomputes the hash.
    // Without a hash the photo cannot collide with another Ready one.
    private Task<int> RefreshReplacedAsync(Guid photoId, ArchiveObjectInfo item, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Photos.Where(p => p.Id == photoId).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.ArchiveETag, item.ETag)
                  .SetProperty(p => p.ArchiveSizeBytes, item.Size)
                  .SetProperty(p => p.RenditionState, ProcessingState.NotStarted)
                  .SetProperty(p => p.Sha256, (string?)null)
                  .SetProperty(p => p.SizeBytes, (long?)null)
                  .SetProperty(p => p.StatusDetail, p => p.Status == PhotoStatus.Missing || p.Status == PhotoStatus.Duplicate ? null : p.StatusDetail)
                  .SetProperty(p => p.Status, p => p.Status == PhotoStatus.Missing || p.Status == PhotoStatus.Duplicate ? PhotoStatus.Ready : p.Status)
                  .SetProperty(p => p.UpdatedAt, now),
            cancellationToken);

    // A photo marked missing whose object is back: Ready again, or Duplicate if its content became Ready elsewhere.
    private Task<int> RestoreAsync(Guid photoId, bool ready, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Photos.Where(p => p.Id == photoId && p.Status == PhotoStatus.Missing).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.Status, ready ? PhotoStatus.Ready : PhotoStatus.Duplicate)
                  .SetProperty(p => p.StatusDetail, ready ? null : DuplicateDetail)
                  .SetProperty(p => p.UpdatedAt, now),
            cancellationToken);

    private async Task<Photo> CreateRecordAsync(ArchiveObjectInfo item, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (ArchiveObjectKeys.TryParseV2PhotoId(item.Key, out var photoId))
        {
            return await RecoverV2RecordAsync(item, photoId, now, cancellationToken);
        }

        var fileName = FileNames.Sanitize(ArchiveObjectKeys.LegacyFileName(item.Key));
        return new Photo
        {
            Id = Guid.CreateVersion7(),
            Source = PhotoSource.Archive,
            Status = PhotoStatus.Ready,
            EncryptionFormat = EncryptionFormat.LegacyV1,
            ArchiveKey = item.Key,
            ArchiveSizeBytes = item.Size,
            ArchiveETag = item.ETag,
            OriginalFileName = fileName,
            ContentType = ArchiveObjectKeys.GuessContentType(fileName),
            UploadedAt = ArchiveObjectKeys.LegacyUploadDate(item.Key) ?? item.LastModified,
            DescriptionState = ProcessingState.NotStarted,
            RenditionState = ProcessingState.NotStarted,
            LastSeenAt = now,
        };
    }

    /// <summary>
    /// Rebuilds a record for a v2 object that has no database row (disaster recovery after losing the
    /// database): the authenticated metadata sealed in the file provides name, description, hash and dates.
    /// </summary>
    private async Task<Photo> RecoverV2RecordAsync(ArchiveObjectInfo item, Guid photoId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // One unreadable object must not stop the whole re-index: the record is still created (it can be inspected
        // and repaired later); only the metadata is missing. An object deleted mid-scan is caught by the next scan.
        EmbeddedPhotoMetadata? metadata = null;
        if (item.Size >= PhotoCipherV2.HeaderSize)
        {
            try
            {
                var prefix = await archive.OpenReadRangeAsync(item.Key, 0, Math.Min(item.Size, PhotoCipherV2.MaxPrefixSize), cancellationToken);
                await using (prefix)
                {
                    metadata = await crypto.ReadArchiveMetadataAsync(prefix, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is PhotoDecryptionException or PhotoKeyUnavailableException or StorageObjectNotFoundException)
            {
                logger.LogWarning(ex, "Could not read embedded metadata of archive object for photo {PhotoId}", photoId);
            }
        }
        else
        {
            logger.LogWarning("Archive object for photo {PhotoId} is too small to be a v2 file ({Size} bytes)", photoId, item.Size);
        }

        var photo = new Photo
        {
            Id = photoId,
            Source = PhotoSource.Upload,
            Status = PhotoStatus.Ready,
            EncryptionFormat = EncryptionFormat.V2,
            ArchiveKey = item.Key,
            ArchiveSizeBytes = item.Size,
            ArchiveETag = item.ETag,
            OriginalFileName = FileNames.Sanitize(metadata?.OriginalFileName ?? $"{photoId:N}"),
            ContentType = metadata?.ContentType ?? "application/octet-stream",
            SizeBytes = metadata?.SizeBytes,
            Sha256 = metadata?.Sha256,
            TakenAt = metadata?.TakenAt,
            UploadedAt = metadata?.UploadedAt ?? item.LastModified,
            UploadedBy = metadata?.UploadedBy,
            RenditionState = ProcessingState.NotStarted,
            DescriptionState = string.IsNullOrEmpty(metadata?.Description) ? ProcessingState.NotStarted : ProcessingState.Ready,
            LastSeenAt = now,
        };

        if (!string.IsNullOrEmpty(metadata?.Description))
        {
            photo.Description = metadata.Description;
            photo.DescriptionSource = DescriptionSource.Gemini;
            photo.SetTags(metadata.Tags);
        }

        return photo;
    }

    private sealed record IndexedPhoto(Guid Id, string ArchiveKey, PhotoStatus Status, string? ArchiveETag, long? ArchiveSizeBytes, string? Sha256);
}
