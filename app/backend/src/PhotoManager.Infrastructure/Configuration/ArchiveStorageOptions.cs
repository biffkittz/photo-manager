using System.ComponentModel.DataAnnotations;

namespace PhotoManager.Infrastructure.Configuration;

/// <summary>Wasabi (S3-compatible) archive bucket settings. Section <c>Archive</c>. Worker tier only.</summary>
public sealed class ArchiveStorageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Archive";

    /// <summary>Gets or sets the S3 endpoint, for example <c>https://s3.us-east-2.wasabisys.com</c>.</summary>
    [Required]
    [Url]
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the signing region of the endpoint.</summary>
    [Required]
    public string Region { get; set; } = "us-east-2";

    /// <summary>Gets or sets the bucket name.</summary>
    [Required]
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Gets or sets the access key id (Key Vault secret <c>Archive--AccessKeyId</c>).</summary>
    [Required]
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>Gets or sets the secret access key (Key Vault secret <c>Archive--SecretAccessKey</c>).</summary>
    [Required]
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether to use path-style URLs (needed by local S3 emulators).</summary>
    public bool ForcePathStyle { get; set; }
}
