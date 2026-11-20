using Azure.Core;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Microsoft.Extensions.Configuration;

namespace PhotoManager.Infrastructure.Configuration;

/// <summary>Configuration sources shared by every host.</summary>
public static class ConfigurationManagerExtensions
{
    /// <summary>
    /// Loads every secret of the tier's Key Vault into configuration when <c>Azure:KeyVaultUri</c> is set.
    /// Secret names use <c>--</c> as the section separator (<c>Crypto--WebKey</c> becomes <c>Crypto:WebKey</c>).
    /// </summary>
    /// <remarks>
    /// Each tier has its own vault (web vs worker) and its identity can read only that vault, so loading
    /// "all secrets" never exposes another tier's keys. Secrets are read once at startup; after rotating a
    /// secret, restart the Container App revision (see docs/operations.md).
    /// </remarks>
    /// <param name="configuration">The configuration manager.</param>
    /// <param name="credential">The Azure credential.</param>
    /// <returns>The same configuration manager.</returns>
    public static IConfigurationManager AddPhotoManagerKeyVault(this IConfigurationManager configuration, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(credential);

        var vaultUri = configuration[$"{AzureOptions.SectionName}:{nameof(AzureOptions.KeyVaultUri)}"];
        if (!string.IsNullOrWhiteSpace(vaultUri))
        {
            configuration.AddAzureKeyVault(new Uri(vaultUri), credential, new AzureKeyVaultConfigurationOptions());
        }

        return configuration;
    }
}
