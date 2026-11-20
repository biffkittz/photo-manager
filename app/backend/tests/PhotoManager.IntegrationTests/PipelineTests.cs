using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using PhotoManager.Core.Crypto;
using PhotoManager.Core.Domain;
using PhotoManager.Core.Workflows;
using PhotoManager.Data.Entities;
using PhotoManager.Infrastructure.Storage;

namespace PhotoManager.IntegrationTests;

[Collection(LocalStackCollection.Name)]
public sealed class PipelineTests(LocalStackFixture stack)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upload_is_described_rendered_archived_and_self_describing()
    {
        Assert.SkipUnless(LocalStackFixture.Enabled, "Set PHOTOMANAGER_INTEGRATION=1 with docker compose running.");
        var jpeg = TestImages.Jpeg();

        var photoId = await StageUploadAsync(jpeg, "Beach Day.JPG");
        await stack.Workflows.StartIngestAsync(photoId, Ct);
        var photo = await stack.WaitForAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.Id == photoId), p => p.Status is not (PhotoStatus.Queued or PhotoStatus.Processing));

        Assert.Equal(PhotoStatus.Ready, photo.Status);
        Assert.Equal(EncryptionFormat.V2, photo.EncryptionFormat);
        Assert.StartsWith("v2/", photo.ArchiveKey, StringComparison.Ordinal);
        Assert.Equal(ProcessingState.Ready, photo.DescriptionState);
        Assert.Contains("placeholder", photo.Description, StringComparison.Ordinal);
        Assert.Equal(ProcessingState.Ready, photo.RenditionState);
        Assert.Equal((1600, 1200), (photo.Width, photo.Height));
        Assert.Equal(new DateTimeOffset(2024, 7, 4, 18, 22, 1, TimeSpan.Zero), photo.TakenAt);
        Assert.False(await stack.Blobs.ExistsAsync(BlobArea.Staging, BlobNames.Staging(photoId), Ct), "staged upload should be deleted");

        // The archive object decrypts (archive key) to the exact original and carries sealed metadata.
        await using (var encrypted = await stack.Archive.OpenReadAsync(photo.ArchiveKey!, Ct))
        await using (var plaintext = (V2DecryptStream)await stack.Crypto.OpenArchiveDecryptStreamAsync(encrypted, EncryptionFormat.V2, Ct))
        {
            using var copy = new MemoryStream();
            await plaintext.CopyToAsync(copy, Ct);
            Assert.Equal(jpeg, copy.ToArray());
            Assert.Equal(photoId, plaintext.Metadata!.PhotoId);
            Assert.Equal(photo.Description, plaintext.Metadata.Description);
            Assert.Equal("Beach Day.JPG", plaintext.Metadata.OriginalFileName); // original casing kept when the extension already matches
        }

        // Renditions are web-key encrypted WebP files.
        await using var thumbnail = await stack.Crypto.OpenWebDecryptStreamAsync(await stack.Blobs.OpenReadAsync(BlobArea.Renditions, BlobNames.Thumbnail(photoId), Ct), Ct);
        var header = new byte[12];
        await thumbnail.ReadExactlyAsync(header, Ct);
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(header, 8, 4));
    }

    [Fact]
    public async Task Duplicate_and_corrupt_uploads_are_rejected_without_archiving()
    {
        Assert.SkipUnless(LocalStackFixture.Enabled, "Set PHOTOMANAGER_INTEGRATION=1 with docker compose running.");
        var jpeg = TestImages.Jpeg(800, 600);
        var first = await StageUploadAsync(jpeg, "first.jpg");
        await stack.Workflows.StartIngestAsync(first, Ct);
        await stack.WaitForAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.Id == first), p => p.Status == PhotoStatus.Ready);

        var duplicate = await StageUploadAsync(jpeg, "copy.jpg");
        await stack.Workflows.StartIngestAsync(duplicate, Ct);
        var duplicatePhoto = await stack.WaitForAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.Id == duplicate), p => p.Status is PhotoStatus.Duplicate or PhotoStatus.Failed or PhotoStatus.Ready);
        Assert.Equal(PhotoStatus.Duplicate, duplicatePhoto.Status);
        Assert.Null(duplicatePhoto.ArchiveKey);

        // JPEG magic bytes followed by garbage: passes sniffing, fails decoding -> permanent failure, no retries.
        var corrupt = await StageUploadAsync([0xFF, 0xD8, 0xFF, 0xE0, .. new byte[5000]], "broken.jpg");
        await stack.Workflows.StartIngestAsync(corrupt, Ct);
        var corruptPhoto = await stack.WaitForAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.Id == corrupt), p => p.Status is PhotoStatus.Failed or PhotoStatus.Ready, TimeSpan.FromSeconds(60));
        Assert.Equal(PhotoStatus.Failed, corruptPhoto.Status);
        Assert.False(string.IsNullOrWhiteSpace(corruptPhoto.StatusDetail));
    }

    [Fact]
    public async Task Legacy_objects_are_indexed_backfilled_described_and_downloadable()
    {
        Assert.SkipUnless(LocalStackFixture.Enabled, "Set PHOTOMANAGER_INTEGRATION=1 with docker compose running.");

        // An object exactly as protect.ps1 would have written it.
        var jpeg = TestImages.Jpeg(1200, 900);
        var key = $"2024-05-01/integration_{Guid.NewGuid():N}_beach_day.jpg.encrypted";
        var temp = Path.GetTempFileName();
        try
        {
            await using (var file = File.Create(temp))
            {
                await LegacyPhotoCipher.EncryptAsync(new MemoryStream(jpeg), file, stack.ArchivePassword, Ct);
            }

            await stack.Archive.UploadFileAsync(key, temp, Ct);
        }
        finally
        {
            File.Delete(temp);
        }

        var reindexJob = await CreateJobAsync(JobKind.ReindexArchive, WorkflowIds.ReindexArchive);
        await stack.Workflows.StartReindexAsync(reindexJob, force: false, Ct);
        await WaitForJobAsync(reindexJob);
        var legacy = await stack.WithDbAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.ArchiveKey == key));
        Assert.Equal(EncryptionFormat.LegacyV1, legacy.EncryptionFormat);
        Assert.Equal(PhotoSource.Archive, legacy.Source);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero), legacy.UploadedAt);
        Assert.Contains("beach", legacy.SearchKeywords, StringComparison.Ordinal);

        var backfill = await CreateJobAsync(JobKind.BackfillRenditions, WorkflowIds.Backfill(JobKind.BackfillRenditions));
        await stack.Workflows.StartBackfillAsync(backfill, JobKind.BackfillRenditions, Ct);
        await WaitForJobAsync(backfill, TimeSpan.FromMinutes(3));
        legacy = await stack.WithDbAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.Id == legacy.Id));
        Assert.Equal(ProcessingState.Ready, legacy.RenditionState);
        Assert.Equal((1200, 900), (legacy.Width, legacy.Height));
        Assert.NotNull(legacy.Sha256);

        await stack.Workflows.StartDescribeAsync(legacy.Id, Ct);
        await stack.WaitForAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.Id == legacy.Id), p => p.DescriptionState == ProcessingState.Ready);

        // A ZIP of the legacy photo plus a fresh upload: originals decrypted by the worker, re-encrypted for the web tier.
        var uploadJpeg = TestImages.Jpeg(640, 480);
        var uploaded = await StageUploadAsync(uploadJpeg, "zip-me.jpg");
        await stack.Workflows.StartIngestAsync(uploaded, Ct);
        await stack.WaitForAsync(db => db.Photos.AsNoTracking().SingleAsync(p => p.Id == uploaded), p => p.Status == PhotoStatus.Ready);

        var downloadId = Guid.CreateVersion7();
        await stack.WithDbAsync(async db =>
        {
            db.Downloads.Add(new PreparedDownload { Id = downloadId, Status = DownloadStatus.Pending, PhotoIds = [legacy.Id, uploaded], RequestedBy = "integration", FileName = "photos.zip", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
            return await db.SaveChangesAsync(Ct);
        });
        await stack.Workflows.StartPrepareDownloadAsync(downloadId, Ct);
        var download = await stack.WaitForAsync(db => db.Downloads.AsNoTracking().SingleAsync(d => d.Id == downloadId), d => d.Status != DownloadStatus.Pending);
        Assert.Equal(DownloadStatus.Ready, download.Status);

        await using var zipPlain = await stack.Crypto.OpenWebDecryptStreamAsync(await stack.Blobs.OpenReadAsync(BlobArea.Downloads, download.BlobName!, Ct), Ct);
        using var zipBytes = new MemoryStream();
        await zipPlain.CopyToAsync(zipBytes, Ct);
        zipBytes.Position = 0;
        using var zip = new ZipArchive(zipBytes, ZipArchiveMode.Read);
        Assert.Equal(2, zip.Entries.Count);
        var legacyEntry = zip.Entries.Single(e => e.Name.Contains("beach_day", StringComparison.Ordinal));
        using var entryCopy = new MemoryStream();
        await using (var entryStream = await legacyEntry.OpenAsync(Ct))
        {
            await entryStream.CopyToAsync(entryCopy, Ct);
        }

        Assert.Equal(jpeg, entryCopy.ToArray());
    }

    /// <summary>Stages an upload exactly like the API's UploadService (web-key encryption into the staging container).</summary>
    private async Task<Guid> StageUploadAsync(byte[] content, string fileName)
    {
        var id = Guid.CreateVersion7();
        var encryptor = await stack.Crypto.CreateWebEncryptStreamAsync(await stack.Blobs.OpenWriteAsync(BlobArea.Staging, BlobNames.Staging(id), Ct), leaveOpen: false, Ct);
        await using (encryptor)
        {
            await encryptor.WriteAsync(content, Ct);
            await encryptor.CompleteAsync(Ct);
        }

        await stack.WithDbAsync(async db =>
        {
            db.Photos.Add(new Photo
            {
                Id = id,
                Source = PhotoSource.Upload,
                Status = PhotoStatus.Queued,
                OriginalFileName = FileNames.WithExtension(FileNames.Sanitize(fileName), ".jpg"),
                ContentType = "image/jpeg",
                UploadedAt = DateTimeOffset.UtcNow,
                UploadedBy = "integration",
                DescriptionState = ProcessingState.Pending,
                RenditionState = ProcessingState.Pending,
            });
            return await db.SaveChangesAsync(Ct);
        });
        return id;
    }

    private Task<Guid> CreateJobAsync(JobKind kind, string workflowId) =>
        stack.WithDbAsync(async db =>
        {
            var job = new MaintenanceJob { Id = Guid.CreateVersion7(), Kind = kind, Status = JobStatus.Queued, WorkflowId = workflowId, RequestedBy = "integration" };
            db.Jobs.Add(job);
            await db.SaveChangesAsync(Ct);
            return job.Id;
        });

    private async Task WaitForJobAsync(Guid jobId, TimeSpan? timeout = null)
    {
        var job = await stack.WaitForAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId), j => j.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled, timeout);
        Assert.True(job.Status == JobStatus.Completed, $"Job {job.Kind} ended {job.Status}: {job.Summary}");
    }
}
