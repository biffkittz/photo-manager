namespace PhotoManager.Infrastructure.Configuration;

/// <summary>
/// Private Azure Blob Storage used for web-key-encrypted staging uploads, renditions and prepared
/// downloads. Section <c>Blob</c>. In Azure, access uses managed identity (shared keys are disabled
/// on the account); a connection string is accepted only for the local Azurite emulator.
/// </summary>
public sealed class BlobStorageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Blob";

    /// <summary>Gets or sets the blob service endpoint, for example <c>https://stphoto.blob.core.windows.net</c>.</summary>
    public string? ServiceUri { get; set; }

    /// <summary>Gets or sets a connection string (local development with Azurite only).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Gets or sets the container for staged uploads (lifecycle-deleted after one day).</summary>
    public string StagingContainer { get; set; } = "staging";

    /// <summary>Gets or sets the container for encrypted thumbnails/previews.</summary>
    public string RenditionsContainer { get; set; } = "renditions";

    /// <summary>Gets or sets the container for prepared downloads (lifecycle-deleted after one day).</summary>
    public string DownloadsContainer { get; set; } = "downloads";

    /// <summary>Gets or sets the container holding the ASP.NET Core Data Protection key ring.</summary>
    public string DataProtectionContainer { get; set; } = "dataprotection";

    /// <summary>Gets or sets a value indicating whether containers are created at startup (local development only).</summary>
    public bool CreateContainers { get; set; }
}
