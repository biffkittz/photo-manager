using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using PhotoManager.Infrastructure.Configuration;

namespace PhotoManager.Infrastructure.Azure;

/// <summary>Creates the <see cref="TokenCredential"/> used for every Azure call (Key Vault, Blob, PostgreSQL).</summary>
public static class AzureCredentialFactory
{
    /// <summary>
    /// In Azure, returns a <see cref="ManagedIdentityCredential"/> pinned to the app's user-assigned identity.
    /// It is pinned rather than using <see cref="DefaultAzureCredential"/> so a production container can never
    /// fall back to another credential source. Locally, returns a developer credential (Azure CLI / VS Code)
    /// for the rare case a developer points the app at real Azure resources.
    /// </summary>
    /// <param name="configuration">App configuration (reads <c>Azure:ManagedIdentityClientId</c>).</param>
    /// <returns>The credential.</returns>
    public static TokenCredential Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var clientId = configuration[$"{AzureOptions.SectionName}:{nameof(AzureOptions.ManagedIdentityClientId)}"];
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            return new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
        }

        return new ChainedTokenCredential(new AzureCliCredential(), new VisualStudioCodeCredential());
    }
}
