namespace PhotoManager.Infrastructure.Configuration;

/// <summary>
/// Key material (section <c>Crypto</c>), loaded from Key Vault in Azure and from user-secrets locally.
/// </summary>
/// <remarks>
/// The web tier must never be configured with <see cref="ArchivePassword"/>; the API refuses to start
/// if it is present. That check keeps the split-key boundary (docs/adr/0001-split-keys.md) from being
/// undone by a configuration mistake.
/// </remarks>
public sealed class CryptoOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Crypto";

    /// <summary>
    /// Default 16-byte PBKDF2 salt for v2 archive keys (base64 of the first 16 bytes of
    /// SHA-256("photo-manager/archive-kdf-salt/v1")). Not secret: it is written into every header.
    /// </summary>
    public const string DefaultArchiveKdfSalt = "O1zP6nn3EyFZaMO35BZ02g==";

    /// <summary>Gets or sets the archive password (worker only; Key Vault secret <c>Crypto--ArchivePassword</c>).</summary>
    public string? ArchivePassword { get; set; }

    /// <summary>Gets or sets the active web key, base64 of 32 random bytes (Key Vault secret <c>Crypto--WebKey</c>).</summary>
    public string? WebKey { get; set; }

    /// <summary>Gets or sets the previous web key during rotation (decrypt only).</summary>
    public string? WebKeyPrevious { get; set; }

    /// <summary>Gets or sets the PBKDF2-SHA256 iteration count for new v2 archive files.</summary>
    public int ArchiveKdfIterations { get; set; } = 600_000;

    /// <summary>Gets or sets the base64 PBKDF2 salt for new v2 archive files.</summary>
    public string ArchiveKdfSalt { get; set; } = DefaultArchiveKdfSalt;
}
