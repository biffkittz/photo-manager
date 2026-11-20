using System.Buffers;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhotoManager.Core.Crypto;
using PhotoManager.Core.Domain;
using PhotoManager.Data;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Imaging;
using PhotoManager.Infrastructure.Storage;
using Temporalio.Activities;

namespace PhotoManager.Worker.Activities;

/// <summary>
/// Activities of the upload ingest pipeline. Every activity is idempotent: Temporal may run an activity
/// more than once (a worker can finish the work and crash before reporting), so each step either checks state
/// first or writes to a deterministic location (the archive key is derived from the photo id).
/// </summary>
public sealed class IngestActivities(
    PhotoDbContext db,
    PlaintextLoader loader,
    PhotoImageProcessor processor,
    PhotoCryptoService crypto,
    IArchiveStore archive,
    IBlobStore blobs,
    IOptions<WorkerOptions> options,
    TimeProvider time)
{
    /// <summary>Decrypts the staged upload, validates it as an image, records its facts, and detects duplicates.</summary>
    /// <param name="photoId">The photo.</param>
    /// <returns>The inspection result.</returns>
    [Activity("InspectStagedUpload")]
    public async Task<InspectResult> InspectStagedUploadAsync(Guid photoId)
    {
        var ct = ActivityExecutionContext.Current.CancellationToken;
        try
        {
            var photo = await db.Photos.SingleOrDefaultAsync(p => p.Id == photoId, ct)
                ?? throw ActivityErrors.Permanent(ActivityErrors.NotFound, "The photo record does not exist.");
            if (photo.Status is not (PhotoStatus.Queued or PhotoStatus.Processing or PhotoStatus.Failed))
            {
                throw ActivityErrors.Permanent(ActivityErrors.InvalidState, $"The photo is {photo.Status}; there is nothing to ingest.");
            }

            using var image = await loader.LoadStagedAsync(photoId, ct);
            var inspection = await processor.InspectAsync(image.Content, ct);
            var duplicateOf = await db.Photos
                .Where(p => p.Sha256 == image.Sha256 && p.Id != photoId && p.Status == PhotoStatus.Ready)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(ct);

            photo.Status = PhotoStatus.Processing;
            photo.StatusDetail = null;
            photo.Sha256 = image.Sha256;
            photo.SizeBytes = image.Length;
            photo.ContentType = inspection.ContentType;
            photo.Width = inspection.Width;
            photo.Height = inspection.Height;
            photo.TakenAt = inspection.TakenAt;
            await db.SaveChangesAsync(ct);

            return new InspectResult(image.Sha256, inspection.ContentType, duplicateOf);
        }
        catch (Exception ex) when (ActivityErrors.TryTranslate(ex, out var failure))
        {
            throw failure;
        }
    }

    /// <summary>
    /// Re-encrypts the staged upload with the archive key (v2, with embedded metadata) and uploads it to the
    /// archive. The plaintext is streamed from staging and hashed on the way; a hash mismatch with the
    /// inspected content aborts the write.
    /// </summary>
    /// <param name="input">Photo id, the inspected content hash and the description to embed.</param>
    /// <returns>The stored object.</returns>
    [Activity("ArchiveOriginal")]
    public async Task<ArchiveResult> ArchiveOriginalAsync(ArchiveOriginalInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ct = ActivityExecutionContext.Current.CancellationToken;
        try
        {
            if (string.IsNullOrEmpty(input.ExpectedSha256))
            {
                throw ActivityErrors.Permanent(ActivityErrors.InvalidState, "The upload has not been inspected.");
            }

            var photo = await db.Photos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == input.PhotoId, ct)
                ?? throw ActivityErrors.Permanent(ActivityErrors.NotFound, "The photo record does not exist.");
            var key = ArchiveObjectKeys.ForV2(photo.Id, photo.UploadedAt);
            var metadata = new EmbeddedPhotoMetadata
            {
                PhotoId = photo.Id,
                OriginalFileName = photo.OriginalFileName,
                ContentType = photo.ContentType,
                SizeBytes = photo.SizeBytes ?? 0,
                Sha256 = input.ExpectedSha256,
                Description = input.Description,
                Tags = input.Tags,
                TakenAt = photo.TakenAt,
                UploadedAt = photo.UploadedAt,
                UploadedBy = photo.UploadedBy,
            };

            // Ciphertext is staged on local disk so the upload has a known length, a Content-MD5 and can be
            // retried by the SDK. Only ciphertext ever touches the disk.
            var tempDirectory = options.Value.TempDirectory ?? Path.GetTempPath();
            Directory.CreateDirectory(tempDirectory);
            var tempPath = Path.Combine(tempDirectory, $"pm-archive-{photo.Id:N}-{Guid.NewGuid():N}.pmv2");
            try
            {
                string actualHash;
                var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

                // On failure the encryptor factory disposes the file stream it was handed (ownership transfers).
                var encryptor = await crypto.CreateArchiveEncryptStreamAsync(file, metadata, leaveOpen: false, ct);
                await using (encryptor)
                {
                    var plaintext = await loader.OpenStagedAsync(photo.Id, ct);
                    await using (plaintext)
                    {
                        actualHash = await CopyAndHashAsync(plaintext, encryptor, options.Value.MaxPlaintextBytes, ct);
                    }

                    // Compared with the hash the inspection computed (passed through the workflow), not with the
                    // database row, which the web tier writes when the upload starts.
                    if (!string.Equals(actualHash, input.ExpectedSha256, StringComparison.Ordinal))
                    {
                        throw PlaintextLoader.StagedContentChanged();
                    }

                    await encryptor.CompleteAsync(ct);
                }

                var stored = await archive.UploadFileAsync(key, tempPath, ct);
                return new ArchiveResult(stored.Key, stored.Size, stored.ETag);
            }
            finally
            {
                // File.Delete ignores a missing file; it is only absent if creating it failed.
                File.Delete(tempPath);
            }
        }
        catch (Exception ex) when (ActivityErrors.TryTranslate(ex, out var failure))
        {
            throw failure;
        }
    }

    /// <summary>Marks the photo ready with its archive location and description, then deletes the staged upload.</summary>
    /// <param name="input">Archive result and description.</param>
    /// <returns><see cref="PhotoStatus.Ready"/>, or <see cref="PhotoStatus.Duplicate"/> when an identical photo became
    /// ready first (two copies uploaded at the same time).</returns>
    [Activity("CompleteIngest")]
    public async Task<PhotoStatus> CompleteIngestAsync(CompleteIngestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var photo = await db.Photos.SingleOrDefaultAsync(p => p.Id == input.PhotoId, ct)
            ?? throw ActivityErrors.Permanent(ActivityErrors.NotFound, "The photo record does not exist.");

        photo.Status = PhotoStatus.Ready;
        photo.StatusDetail = null;
        photo.EncryptionFormat = EncryptionFormat.V2;
        photo.ArchiveKey = input.Archive.Key;
        photo.ArchiveSizeBytes = input.Archive.Size;
        photo.ArchiveETag = input.Archive.ETag;
        photo.LastSeenAt = time.GetUtcNow();
        if (input.Description is { } description)
        {
            photo.Description = description.Description;
            photo.DescriptionSource = DescriptionSource.Gemini;
            photo.DescriptionModel = description.Model;
            photo.SetTags(description.Tags);
            photo.DescriptionState = ProcessingState.Ready;
        }
        else
        {
            // Skipped: not attempted, so not a failure; "Generate missing descriptions" or the photo's Describe button
            // can describe it later (that description is then stored in the database only, not sealed in the file).
            photo.DescriptionState = input.DescriptionSkipped ? ProcessingState.NotStarted : ProcessingState.Failed;
        }

        var status = PhotoStatus.Ready;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, DbErrors.UniqueReadyContentIndex))
        {
            // The same content was uploaded twice at once and the other copy won. Keep this record (it points
            // at its own archived object) but hide it as a duplicate.
            db.ChangeTracker.Clear();
            var now = time.GetUtcNow();
            await db.Photos.Where(p => p.Id == input.PhotoId).ExecuteUpdateAsync(
                s => s.SetProperty(p => p.Status, PhotoStatus.Duplicate)
                      .SetProperty(p => p.StatusDetail, "This photo is already in the library.")
                      .SetProperty(p => p.EncryptionFormat, EncryptionFormat.V2)
                      .SetProperty(p => p.ArchiveKey, input.Archive.Key)
                      .SetProperty(p => p.ArchiveSizeBytes, input.Archive.Size)
                      .SetProperty(p => p.ArchiveETag, input.Archive.ETag)
                      .SetProperty(p => p.LastSeenAt, now)
                      .SetProperty(p => p.UpdatedAt, now),
                ct);
            status = PhotoStatus.Duplicate;
        }

        await blobs.DeleteIfExistsAsync(BlobArea.Staging, BlobNames.Staging(input.PhotoId), ct);
        return status;
    }

    /// <summary>Ends an ingest that will not be archived (duplicate or permanent failure).</summary>
    /// <param name="input">Outcome details.</param>
    /// <returns>A task that completes when the record is updated.</returns>
    [Activity("FinishIngestWithoutArchive")]
    public async Task FinishIngestWithoutArchiveAsync(IngestOutcomeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ct = ActivityExecutionContext.Current.CancellationToken;
        var now = time.GetUtcNow();
        var detail = input.Detail.Length > 500 ? input.Detail[..500] : input.Detail;

        // A photo that already reached a final state is never downgraded (for example by a stray second run).
        await db.Photos.Where(p => p.Id == input.PhotoId && p.Status != PhotoStatus.Ready && p.Status != PhotoStatus.Duplicate).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.Status, input.Status)
                  .SetProperty(p => p.StatusDetail, detail)
                  .SetProperty(p => p.UpdatedAt, now),
            ct);

        if (input.DeleteStaging)
        {
            await blobs.DeleteIfExistsAsync(BlobArea.Staging, BlobNames.Staging(input.PhotoId), ct);
        }
    }

    private static async Task<string> CopyAndHashAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            long total = 0;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                // The staged blob is web-key data the internet-facing tier can write; never let a swapped,
                // oversized blob fill the worker's disk before the hash check at the end rejects it.
                total += read;
                if (total > maxBytes)
                {
                    throw ActivityErrors.Permanent(ActivityErrors.TooLarge, "The photo is larger than the maximum supported size.");
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
