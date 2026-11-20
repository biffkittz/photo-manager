using System.ComponentModel.DataAnnotations;

namespace PhotoManager.Infrastructure.Configuration;

/// <summary>Temporal connection settings. Section <c>Temporal</c>.</summary>
public sealed class TemporalOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Temporal";

    /// <summary>Gets or sets the gRPC endpoint, for example <c>photo.joqqs.tmprl.cloud:7233</c> or <c>localhost:7233</c>.</summary>
    [Required]
    public string Address { get; set; } = "localhost:7233";

    /// <summary>Gets or sets the namespace, for example <c>photo.joqqs</c>.</summary>
    [Required]
    public string Namespace { get; set; } = "default";

    /// <summary>Gets or sets the Temporal Cloud API key (Key Vault secret <c>Temporal--ApiKey</c>). Enables TLS.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets a value indicating whether to use TLS without an API key.</summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Gets or sets the base64 256-bit key of the payload codec (Key Vault secret <c>Temporal--PayloadKey</c>).
    /// Every workflow/activity payload is AES-GCM encrypted with it before leaving the process, so Temporal
    /// Cloud never sees filenames, descriptions or other photo metadata.
    /// </summary>
    [Required]
    public string PayloadKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the previous payload key during rotation (decrypt only).</summary>
    public string? PayloadKeyPrevious { get; set; }
}
