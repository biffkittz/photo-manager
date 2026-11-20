namespace PhotoManager.Api.Uploads;

/// <summary>Upload limits. Section <c>Uploads</c>.</summary>
public sealed class UploadOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Uploads";

    /// <summary>Gets or sets the largest accepted file.</summary>
    public long MaxFileSizeBytes { get; set; } = 100L * 1024 * 1024;
}
