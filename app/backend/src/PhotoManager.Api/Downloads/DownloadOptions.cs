namespace PhotoManager.Api.Downloads;

/// <summary>Download preparation limits. Section <c>Downloads</c>.</summary>
public sealed class DownloadOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Downloads";

    /// <summary>Gets or sets the most photos a single download may contain.</summary>
    public int MaxPhotosPerDownload { get; set; } = 500;

    /// <summary>Gets or sets the largest aggregate original size allowed in one download (default 4 GiB).</summary>
    public long MaxDownloadBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>Gets or sets the most active pending downloads a single user may have preparing at once.</summary>
    public int MaxPendingDownloadsPerUser { get; set; } = 3;
}
