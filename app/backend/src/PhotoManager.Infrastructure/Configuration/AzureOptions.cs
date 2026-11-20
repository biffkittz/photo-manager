namespace PhotoManager.Infrastructure.Configuration;

/// <summary>Azure identity and Key Vault settings. Section <c>Azure</c>.</summary>
public sealed class AzureOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Azure";

    /// <summary>Gets or sets the client id of the user-assigned managed identity (set by Terraform in Azure).</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>Gets or sets the Key Vault URI whose secrets are loaded into configuration.</summary>
    public string? KeyVaultUri { get; set; }
}
