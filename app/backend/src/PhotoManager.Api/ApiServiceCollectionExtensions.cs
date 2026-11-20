using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using PhotoManager.Api.Audit;
using PhotoManager.Api.Auth;
using PhotoManager.Api.Security;
using PhotoManager.Api.Uploads;
using PhotoManager.Infrastructure.Configuration;
using PhotoManager.Infrastructure.Health;
using PhotoManager.Infrastructure.Storage;

namespace PhotoManager.Api;

/// <summary>Web-tier service registration.</summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>Registers API services (auth, storage, data protection, limits, JSON, health).</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">App configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <param name="credential">Azure credential.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPhotoApi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<BlobStorageOptions>().BindConfiguration(BlobStorageOptions.SectionName);
        services.AddOptions<UploadOptions>().BindConfiguration(UploadOptions.SectionName);
        services.AddSingleton<AzureBlobStore>();
        services.AddSingleton<IBlobStore>(sp => sp.GetRequiredService<AzureBlobStore>());
        services.AddScoped<UploadService>();
        services.AddScoped<AuditLog>();
        services.AddHttpContextAccessor();

        services.AddPhotoAuthentication(configuration, environment);
        services.AddPhotoRateLimiting();
        services.AddProblemDetails();
        services.AddPhotoHealthChecks();
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Azure Container Apps terminates TLS at its Envoy ingress and forwards X-Forwarded-For/-Proto. The
        // container is reachable only through that ingress, so the forwarded headers are trusted; without this
        // the OAuth redirect URI would be http:// and client IPs would all be the proxy's.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        AddDataProtection(services, configuration, environment, credential);
        return services;
    }

    /// <summary>
    /// Data Protection encrypts the session, correlation and antiforgery cookies. Its key ring must be shared by
    /// all replicas and survive restarts: in Azure it lives in a private blob, wrapped by a Key Vault key the web
    /// identity can only use (wrap/unwrap), never export.
    /// </summary>
    private static void AddDataProtection(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, TokenCredential credential)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("photo-manager");
        var keyId = configuration["DataProtection:KeyVaultKeyId"];
        var blob = configuration.GetSection(BlobStorageOptions.SectionName).Get<BlobStorageOptions>() ?? new BlobStorageOptions();

        if (!string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(blob.ServiceUri))
        {
            dataProtection
                .PersistKeysToAzureBlobStorage(new Uri($"{blob.ServiceUri.TrimEnd('/')}/{blob.DataProtectionContainer}/keys.xml"), credential)
                .ProtectKeysWithAzureKeyVault(new Uri(keyId), credential);
        }
        else if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            // Local development and the API test suite ("Testing" runs production cookie settings over https).
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "photo-manager-dev-keys")));
        }
        else
        {
            throw new InvalidOperationException("DataProtection:KeyVaultKeyId and Blob:ServiceUri are required outside Development.");
        }
    }
}
