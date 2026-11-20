using System.Buffers;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhotoManager.Api.Audit;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Security;
using PhotoManager.Core.Domain;
using PhotoManager.Data;
using PhotoManager.Data.Entities;
using PhotoManager.Infrastructure.Crypto;
using PhotoManager.Infrastructure.Storage;
using PhotoManager.Infrastructure.Temporal;

namespace PhotoManager.Api.Uploads;

/// <summary>
/// Stages uploads for the ingest workflow. The request body is streamed straight into web-key encryption
/// and then to private blob storage, so the plaintext is never buffered whole or written to disk, and staged
/// data is encrypted at rest (on top of Azure Storage encryption). The archive encryption, the Gemini call and
/// all other processing happen on the workers.
/// </summary>
public sealed class UploadService(
    PhotoDbContext db,
    IBlobStore blobs,
    PhotoCryptoService crypto,
    PhotoWorkflowClient workflows,
    AuditLog audit,
    IOptions<UploadOptions> options,
    TimeProvider time,
    ILogger<UploadService> logger)
{
    /// <summary>Stages one file.</summary>
    /// <param name="content">The file body (untrusted).</param>
    /// <param name="clientFileName">The client-supplied filename (untrusted; sanitized).</param>
    /// <param name="batchId">Client-generated batch id grouping one upload session.</param>
    /// <param name="uploadedBy">GitHub login or member name of the uploader (display).</param>
    /// <param name="uploadedById">User id of the uploader (ownership; see <c>PhotoClaims.UserId</c>).</param>
    /// <param name="describe">False when the uploader switched AI descriptions off: the photo is archived without
    /// waiting for Gemini and left for a later description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<UploadOutcome> StageAsync(Stream content, string? clientFileName, Guid? batchId, string uploadedBy, long uploadedById, bool describe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Decide the type from the bytes, never from the filename or the browser's Content-Type.
        var prefix = new byte[ImageSignatures.RequiredPrefixLength];
        var prefixLength = await content.ReadAtLeastAsync(prefix, prefix.Length, throwOnEndOfStream: false, cancellationToken);
        var detected = ImageSignatures.Detect(prefix.AsSpan(0, prefixLength));
        if (detected is null)
        {
            return Reject(StatusCodes.Status415UnsupportedMediaType, "Only JPEG, PNG, GIF and WebP images are supported.");
        }

        var photoId = Guid.CreateVersion7();
        var blobName = BlobNames.Staging(photoId);
        var max = options.Value.MaxFileSizeBytes;
        Photo photo;
        long size;
        var staged = false;
        try
        {
            string sha256;
            var blobStream = await blobs.OpenWriteAsync(BlobArea.Staging, blobName, cancellationToken);
            staged = true;
            var encryptor = await crypto.CreateWebEncryptStreamAsync(blobStream, leaveOpen: false, cancellationToken);
            await using (encryptor)
            {
                (sha256, size) = await CopyWithLimitAsync(prefix.AsMemory(0, prefixLength), content, encryptor, max, cancellationToken);
                await encryptor.CompleteAsync(cancellationToken);
            }

            // Fast duplicate feedback (also catches the same file twice in one batch); the database's unique index
            // on ready content is the authoritative guard against races.
            var existing = await db.Photos.AsNoTracking()
                .Where(p => p.Sha256 == sha256 && (p.Status == PhotoStatus.Ready || p.Status == PhotoStatus.Queued || p.Status == PhotoStatus.Processing))
                .Select(p => (PhotoStatus?)p.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                await TryDeleteStagingBlobAsync(blobName);
                return Reject(
                    StatusCodes.Status409Conflict,
                    existing == PhotoStatus.Ready ? "This photo is already in the library." : "This photo is already being uploaded.");
            }

            photo = new Photo
            {
                Id = photoId,
                Source = PhotoSource.Upload,
                Status = PhotoStatus.Queued,
                OriginalFileName = FileNames.WithExtension(FileNames.Sanitize(clientFileName), detected.Extension),
                ContentType = detected.ContentType,
                SizeBytes = size,
                Sha256 = sha256,
                UploadedAt = time.GetUtcNow(),
                UploadedBy = uploadedBy,
                UploadedById = uploadedById,
                UploadBatchId = batchId,
                DescriptionState = describe ? ProcessingState.Pending : ProcessingState.NotStarted,
                RenditionState = ProcessingState.Pending,
            };
            db.Photos.Add(photo);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (UploadTooLargeException)
        {
            await TryDeleteStagingBlobAsync(blobName);
            return Reject(StatusCodes.Status413PayloadTooLarge, $"Files are limited to {max / (1024 * 1024)} MB.");
        }
        catch
        {
            if (staged)
            {
                await TryDeleteStagingBlobAsync(blobName);
            }

            throw;
        }

        await audit.WriteAsync("photo.upload", photoId.ToString(), new { size, type = detected.ContentType, describe });

        try
        {
            await workflows.StartIngestAsync(photoId, skipDescription: !describe);
        }
        catch (Exception ex) when (TemporalUnavailable.Matches(ex))
        {
            logger.LogError(ex, "Could not start ingest for photo {PhotoId}", photoId);
            photo.Status = PhotoStatus.Failed;
            photo.StatusDetail = "Processing could not be queued. Retry from the Uploads page.";
            await db.SaveChangesAsync(CancellationToken.None);
            return Reject(StatusCodes.Status503ServiceUnavailable, photo.StatusDetail);
        }

        return new UploadOutcome(new UploadAccepted(photoId, photo.Status), null);
    }

    /// <summary>Re-queues a failed upload whose staged copy still exists (staging expires after a day).</summary>
    /// <param name="photoId">The photo.</param>
    /// <param name="requestedById">User id of the requester; only the uploader may retry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The HTTP result.</returns>
    public async Task<IResult> RetryAsync(Guid photoId, long requestedById, CancellationToken cancellationToken)
    {
        var photo = await db.Photos.SingleOrDefaultAsync(p => p.Id == photoId && p.UploadedById == requestedById, cancellationToken);
        if (photo is null)
        {
            return TypedResults.NotFound();
        }

        if (photo.Status != PhotoStatus.Failed || !await blobs.ExistsAsync(BlobArea.Staging, BlobNames.Staging(photoId), cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "This upload can no longer be retried; upload the file again.");
        }

        var previousDetail = photo.StatusDetail;
        photo.Status = PhotoStatus.Queued;
        photo.StatusDetail = null;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A second click (or another tab) got there first.
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "This upload is already being retried.");
        }

        try
        {
            // A retry keeps the uploader's choice: photos uploaded without a description are "not started".
            await workflows.StartIngestAsync(photoId, skipDescription: photo.DescriptionState == ProcessingState.NotStarted);
        }
        catch (Exception ex) when (TemporalUnavailable.Matches(ex))
        {
            // Back to Failed (with its message), so the Retry button keeps working once Temporal is reachable.
            logger.LogError(ex, "Could not restart ingest for photo {PhotoId}", photoId);
            photo.Status = PhotoStatus.Failed;
            photo.StatusDetail = previousDetail ?? "Processing could not be queued. Try again shortly.";
            await db.SaveChangesAsync(CancellationToken.None);
            return TemporalUnavailable.Problem();
        }

        await audit.WriteAsync("photo.upload.retry", photoId.ToString(), null);
        return TypedResults.Accepted((string?)null, new UploadAccepted(photoId, photo.Status));
    }

    private async Task TryDeleteStagingBlobAsync(string blobName)
    {
#pragma warning disable CA1031 // Best-effort cleanup must never mask the upload rejection or root failure.
        try
        {
            await blobs.DeleteIfExistsAsync(BlobArea.Staging, blobName, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete abandoned staging blob {BlobName}", blobName);
        }
#pragma warning restore CA1031
    }

    private static UploadOutcome Reject(int status, string title) =>
        new(null, TypedResults.Problem(statusCode: status, title: title));

    private static async Task<(string Sha256, long Size)> CopyWithLimitAsync(
        ReadOnlyMemory<byte> prefix,
        Stream source,
        Stream destination,
        long max,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(prefix.Span);
        await destination.WriteAsync(prefix, cancellationToken);
        long total = prefix.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > max)
                {
                    throw new UploadTooLargeException();
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        return (Convert.ToHexStringLower(hash.GetHashAndReset()), total);
    }

    private sealed class UploadTooLargeException : Exception;
}
