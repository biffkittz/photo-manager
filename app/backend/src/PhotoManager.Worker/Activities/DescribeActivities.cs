using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PhotoManager.Core.Domain;
using PhotoManager.Data;
using PhotoManager.Infrastructure.Configuration;
using PhotoManager.Infrastructure.Gemini;
using PhotoManager.Infrastructure.Imaging;
using Temporalio.Activities;

namespace PhotoManager.Worker.Activities;

/// <summary>
/// Gemini description activities. They run only on the <c>photo-ai</c> task queue, whose server-side rate
/// limit keeps all replicas together under the Gemini quota. The model receives a downscaled JPEG with no
/// EXIF/GPS metadata; the original never leaves the worker.
/// </summary>
public sealed class DescribeActivities(
    PhotoDbContext db,
    PlaintextLoader loader,
    PhotoImageProcessor processor,
    IPhotoDescriber describer,
    IOptions<GeminiOptions> gemini,
    TimeProvider time,
    ILogger<DescribeActivities> logger)
{
    /// <summary>Describes a staged upload before it is encrypted into the archive.</summary>
    /// <param name="input">The photo and the hash its inspection computed.</param>
    /// <returns>The description (embedded into the archive file by the next step).</returns>
    [Activity("DescribeStagedUpload")]
    public async Task<DescribeResult> DescribeStagedUploadAsync(StagedUploadInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ct = ActivityExecutionContext.Current.CancellationToken;
        try
        {
            using var image = await loader.LoadStagedAsync(input.PhotoId, input.ExpectedSha256, ct);
            return await DescribeAsync(image, ct);
        }
        catch (Exception ex) when (ActivityErrors.TryTranslate(ex, out var failure))
        {
            throw failure;
        }
    }

    /// <summary>Describes an archived photo (backfill or user-requested regeneration) and stores the result.</summary>
    /// <param name="input">The photo, and whether a description written by a person must be kept.</param>
    /// <returns>A task that completes when the description is stored (or deliberately not stored).</returns>
    [Activity("DescribeArchivedPhoto")]
    public async Task DescribeArchivedPhotoAsync(DescribeArchivedInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ct = ActivityExecutionContext.Current.CancellationToken;
        try
        {
            var photo = await db.Photos.SingleOrDefaultAsync(p => p.Id == input.PhotoId, ct)
                ?? throw ActivityErrors.Permanent(ActivityErrors.NotFound, "The photo record does not exist.");
            if (input.KeepUserDescription && photo.DescriptionSource == DescriptionSource.User)
            {
                return;
            }

            using var image = await loader.LoadArchivedAsync(photo, ct);
            var result = await DescribeAsync(image, ct);

            // Gemini can take minutes: someone may have written a description meanwhile. Re-read before writing;
            // the row version then guards the short window until the save.
            await db.Entry(photo).ReloadAsync(ct);
            if (input.KeepUserDescription && photo.DescriptionSource == DescriptionSource.User)
            {
                return;
            }

            photo.Description = result.Description;
            photo.DescriptionSource = DescriptionSource.Gemini;
            photo.DescriptionModel = result.Model;
            photo.SetTags(result.Tags);
            photo.DescriptionState = ProcessingState.Ready;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ActivityErrors.TryTranslate(ex, out var failure))
        {
            throw failure;
        }
    }

    /// <summary>Records that describing failed (the photo stays usable without a description).</summary>
    /// <param name="photoId">The photo.</param>
    /// <returns>A task that completes when the record is updated.</returns>
    [Activity("MarkDescriptionFailed")]
    public async Task MarkDescriptionFailedAsync(Guid photoId)
    {
        var now = time.GetUtcNow();
        await db.Photos.Where(p => p.Id == photoId && p.DescriptionState != ProcessingState.Ready).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.DescriptionState, ProcessingState.Failed).SetProperty(p => p.UpdatedAt, now),
            ActivityExecutionContext.Current.CancellationToken);
    }

    private async Task<DescribeResult> DescribeAsync(LoadedImage image, CancellationToken cancellationToken)
    {
        var jpeg = await processor.CreateAiInputAsync(image.Content, gemini.Value.MaxImageDimension, cancellationToken);
        var started = time.GetTimestamp();
        var description = await describer.DescribeAsync(jpeg, cancellationToken);

        // Failures are logged by GeminiPhotoDescriber; logging successes too makes Gemini's success rate visible
        // (docs/operations.md). Never the description itself.
        logger.LogInformation(
            "Gemini described a photo in {Seconds:F1} s with {Model}",
            time.GetElapsedTime(started).TotalSeconds,
            description.Model);
        return new DescribeResult(description.Description, description.Tags, description.Model);
    }
}
