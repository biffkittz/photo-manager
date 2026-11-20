using Microsoft.EntityFrameworkCore;
using PhotoManager.Core.Domain;
using PhotoManager.Data;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Imaging;
using PhotoManager.Infrastructure.Storage;
using Temporalio.Activities;

namespace PhotoManager.Worker.Activities;

/// <summary>
/// Creates thumbnail/preview renditions. Renditions are encrypted with the <em>web key</em>, not the archive
/// key: that is what lets the internet-facing API serve the gallery without ever holding the archive password.
/// </summary>
public sealed class RenditionActivities(
    PhotoDbContext db,
    PlaintextLoader loader,
    PhotoImageProcessor processor,
    PhotoCryptoService crypto,
    IBlobStore blobs,
    TimeProvider time)
{
    /// <summary>Creates renditions from a staged upload (during ingest).</summary>
    /// <param name="photoId">The photo.</param>
    /// <returns>A task that completes when the renditions are stored.</returns>
    [Activity("CreateRenditionsFromStaging")]
    public async Task CreateRenditionsFromStagingAsync(Guid photoId)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        try
        {
            using var image = await loader.LoadStagedAsync(photoId, ct);
            await StoreAsync(photoId, image, ct);
            var now = time.GetUtcNow();
            await db.Photos.Where(p => p.Id == photoId).ExecuteUpdateAsync(
                s => s.SetProperty(p => p.RenditionState, ProcessingState.Ready)
                      .SetProperty(p => p.RenditionVersion, p => p.RenditionVersion + 1)
                      .SetProperty(p => p.UpdatedAt, now),
                ct);
        }
        catch (Exception ex) when (ActivityErrors.TryTranslate(ex, out var failure))
        {
            throw failure;
        }
    }

    /// <summary>
    /// Creates renditions from an archive original (backfill and on-demand). For legacy photos this is the
    /// first time the content is decoded, so dimensions, capture date and content hash are recorded too, and a
    /// byte-identical copy of a photo already in the library is hidden as a duplicate instead of rendered.
    /// </summary>
    /// <param name="photoId">The photo.</param>
    /// <returns>Whether renditions were created or the photo turned out to be a duplicate.</returns>
    [Activity("CreateRenditionsFromArchive")]
    public async Task<RenditionOutcome> CreateRenditionsFromArchiveAsync(Guid photoId)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        try
        {
            var photo = await db.Photos.SingleOrDefaultAsync(p => p.Id == photoId, ct)
                ?? throw ActivityErrors.Permanent(ActivityErrors.NotFound, "The photo record does not exist.");

            using var image = await loader.LoadArchivedAsync(photo, ct);

            // The archive may hold the same photo twice (for example protect.ps1 run from two machines). The
            // library shows it once; the archive objects themselves are never touched.
            if (photo.Status == PhotoStatus.Ready && await FindReadyCopyAsync(photoId, image.Sha256, ct) is { } original)
            {
                await MarkDuplicateAsync(photoId, image, original, ct);
                return RenditionOutcome.Duplicate;
            }

            var inspection = await processor.InspectAsync(image.Content, ct);
            await StoreAsync(photoId, image, ct);

            photo.Width = inspection.Width;
            photo.Height = inspection.Height;
            photo.ContentType = inspection.ContentType;
            photo.TakenAt ??= inspection.TakenAt;
            photo.Sha256 = image.Sha256;
            photo.SizeBytes ??= image.Length;
            photo.RenditionState = ProcessingState.Ready;
            photo.RenditionVersion++;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, DbErrors.UniqueReadyContentIndex))
            {
                // An identical copy was processed concurrently (backfills run several photos at once) and became
                // the library copy first.
                db.ChangeTracker.Clear();
                await MarkDuplicateAsync(photoId, image, await FindReadyCopyAsync(photoId, image.Sha256, ct), ct);
                return RenditionOutcome.Duplicate;
            }

            return RenditionOutcome.Created;
        }
        catch (UnsupportedImageException ex)
        {
            await MarkAsync(photoId, ProcessingState.Unsupported, ct);
            throw ActivityErrors.Permanent(ActivityErrors.UnsupportedImage, ex.Message, ex);
        }
        catch (Exception ex) when (ActivityErrors.TryTranslate(ex, out var failure))
        {
            throw failure;
        }
    }

    /// <summary>Records that rendition generation failed permanently (the gallery shows a placeholder).</summary>
    /// <param name="photoId">The photo.</param>
    /// <returns>A task that completes when the record is updated.</returns>
    [Activity("MarkRenditionsFailed")]
    public Task MarkRenditionsFailedAsync(Guid photoId) =>
        MarkAsync(photoId, ProcessingState.Failed, ActivityExecutionContext.Current.CancellationToken);

    private async Task MarkAsync(Guid photoId, ProcessingState state, CancellationToken cancellationToken)
    {
        // Ready and Unsupported are final: a later generic failure must not turn "unsupported" into "failed",
        // which backfills would retry forever.
        var now = time.GetUtcNow();
        await db.Photos.Where(p => p.Id == photoId && p.RenditionState != ProcessingState.Ready && p.RenditionState != ProcessingState.Unsupported).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.RenditionState, state).SetProperty(p => p.UpdatedAt, now),
            cancellationToken);
    }

    private Task<string?> FindReadyCopyAsync(Guid photoId, string sha256, CancellationToken cancellationToken) =>
        db.Photos.AsNoTracking()
            .Where(p => p.Sha256 == sha256 && p.Id != photoId && p.Status == PhotoStatus.Ready)
            .OrderBy(p => p.UploadedAt)
            .Select(p => (string?)p.OriginalFileName)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task MarkDuplicateAsync(Guid photoId, LoadedImage image, string? originalName, CancellationToken cancellationToken)
    {
        var detail = originalName is null ? "Identical to another photo in the library." : $"Identical to {originalName}.";
        detail = detail.Length > 500 ? detail[..500] : detail;
        var now = time.GetUtcNow();
        await db.Photos.Where(p => p.Id == photoId && p.Status == PhotoStatus.Ready).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.Status, PhotoStatus.Duplicate)
                  .SetProperty(p => p.StatusDetail, detail)
                  .SetProperty(p => p.Sha256, image.Sha256)
                  .SetProperty(p => p.SizeBytes, image.Length)
                  .SetProperty(p => p.UpdatedAt, now),
            cancellationToken);
    }

    private async Task StoreAsync(Guid photoId, LoadedImage image, CancellationToken cancellationToken)
    {
        var renditions = await processor.CreateRenditionsAsync(image.Content, cancellationToken);
        await blobs.UploadAsync(BlobArea.Renditions, BlobNames.Thumbnail(photoId), await crypto.EncryptWithWebKeyAsync(renditions.Thumbnail, cancellationToken), cancellationToken);
        await blobs.UploadAsync(BlobArea.Renditions, BlobNames.Preview(photoId), await crypto.EncryptWithWebKeyAsync(renditions.Preview, cancellationToken), cancellationToken);
    }
}
