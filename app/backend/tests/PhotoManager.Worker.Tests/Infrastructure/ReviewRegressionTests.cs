using System.IO.Compression;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using PhotoManager.Infrastructure.Configuration;
using PhotoManager.Infrastructure.Gemini;
using PhotoManager.Infrastructure.Imaging;
using PhotoManager.Worker.Activities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoManager.Worker.Tests.Infrastructure;

/// <summary>Regressions found by the 2026-11-19 review: dates, retry hints and sweep limits.</summary>
public sealed class ReviewRegressionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("+02:00", 120)]
    [InlineData("-06:00", -360)]
    [InlineData("-03:30", -210)]
    [InlineData("05:45", 345)]
    [InlineData("+14:00", 840)]
    public void Parses_exif_offsets_with_either_sign(string value, int minutes)
    {
        Assert.True(PhotoImageProcessor.TryParseUtcOffset(value, out var offset));
        Assert.Equal(TimeSpan.FromMinutes(minutes), offset);
    }

    [Theory]
    [InlineData("+15:00")]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_invalid_exif_offsets(string? value) =>
        Assert.False(PhotoImageProcessor.TryParseUtcOffset(value, out _));

    [Fact]
    public async Task Capture_time_honours_negative_offsets()
    {
        // 18:30 in Chicago (UTC-6) on July 4 is 00:30 UTC on July 5; it used to be read as 18:30 UTC.
        using var image = new Image<Rgba32>(8, 8);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.DateTimeOriginal, "2024:07:04 18:30:00");
        image.Metadata.ExifProfile.SetValue(ExifTag.OffsetTimeOriginal, "-06:00");
        using var jpeg = new MemoryStream();
        await image.SaveAsJpegAsync(jpeg, Ct);
        jpeg.Position = 0;

        var inspection = await new PhotoImageProcessor(Options.Create(new ImagingOptions())).InspectAsync(jpeg, Ct);

        Assert.Equal(new DateTimeOffset(2024, 7, 5, 0, 30, 0, TimeSpan.Zero), inspection.TakenAt!.Value.ToUniversalTime());
    }

    [Fact]
    public void Retry_after_supports_delays_and_http_dates()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(30), GeminiPhotoDescriber.RetryDelay(new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)), now));
        Assert.Equal(TimeSpan.FromSeconds(90), GeminiPhotoDescriber.RetryDelay(new RetryConditionHeaderValue(now.AddSeconds(90)), now));
        Assert.Equal(TimeSpan.Zero, GeminiPhotoDescriber.RetryDelay(new RetryConditionHeaderValue(now.AddSeconds(-5)), now));
        Assert.Null(GeminiPhotoDescriber.RetryDelay(null, now));
    }

    [Theory]
    [InlineData(1975, 1980)]
    [InlineData(2024, 2024)]
    [InlineData(2150, 2107)]
    public void Zip_timestamps_are_clamped_to_the_range_zip_can_store(int year, int expectedYear)
    {
        var stamp = DownloadActivities.ZipTimestamp(new DateTimeOffset(year, 6, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(expectedYear, stamp.Year);
        using var zip = new ZipArchive(new MemoryStream(), ZipArchiveMode.Create);
        zip.CreateEntry("photo.jpg").LastWriteTime = stamp; // threw ArgumentOutOfRangeException for pre-1980 dates
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(20, 3)]
    [InlineData(50, 5)]
    [InlineData(5000, 500)]
    public void Sweep_limit_is_ten_percent_with_a_small_floor(int total, int expected) =>
        Assert.Equal(expected, MaintenanceActivities.SweepLimit(total));
}
