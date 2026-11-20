using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using PhotoManager.Infrastructure.Configuration;

namespace PhotoManager.Infrastructure.Storage;

/// <summary><see cref="IBlobStore"/> backed by Azure Blob Storage (or Azurite locally).</summary>
public sealed class AzureBlobStore : IBlobStore
{
    private readonly BlobServiceClient _service;
    private readonly BlobStorageOptions _options;

    /// <summary>Initializes a new instance of the <see cref="AzureBlobStore"/> class.</summary>
    /// <param name="options">Blob options.</param>
    /// <param name="credential">Managed identity credential (unused with a connection string).</param>
    public AzureBlobStore(IOptions<BlobStorageOptions> options, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _service = CreateServiceClient(_options, credential);
    }

    /// <summary>Creates the service client from options: connection string (Azurite) or endpoint + identity (Azure).</summary>
    /// <param name="options">Blob options.</param>
    /// <param name="credential">Azure credential.</param>
    /// <returns>The client.</returns>
    public static BlobServiceClient CreateServiceClient(BlobStorageOptions options, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new BlobServiceClient(options.ConnectionString);
        }

        if (string.IsNullOrWhiteSpace(options.ServiceUri))
        {
            throw new InvalidOperationException("Blob:ServiceUri (or Blob:ConnectionString for Azurite) is required.");
        }

        return new BlobServiceClient(new Uri(options.ServiceUri), credential);
    }

    /// <summary>Creates the containers if missing. Only used for local development; Terraform owns them in Azure.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the containers exist.</returns>
    public async Task EnsureContainersAsync(CancellationToken cancellationToken)
    {
        foreach (var name in new[] { _options.StagingContainer, _options.RenditionsContainer, _options.DownloadsContainer, _options.DataProtectionContainer })
        {
            await _service.GetBlobContainerClient(name).CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(BlobArea area, string name, CancellationToken cancellationToken)
    {
        try
        {
            return await Blob(area, name).OpenReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new StorageObjectNotFoundException($"Blob {area}/{name} was not found.", ex);
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenWriteAsync(BlobArea area, string name, CancellationToken cancellationToken) =>
        Blob(area, name).OpenWriteAsync(overwrite: true, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task UploadAsync(BlobArea area, string name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        await Blob(area, name).UploadAsync(BinaryData.FromBytes(content), overwrite: true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(BlobArea area, string name, CancellationToken cancellationToken)
    {
        var response = await Blob(area, name).ExistsAsync(cancellationToken).ConfigureAwait(false);
        return response.Value;
    }

    /// <inheritdoc />
    public async Task DeleteIfExistsAsync(BlobArea area, string name, CancellationToken cancellationToken)
    {
        await Blob(area, name).DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private BlobClient Blob(BlobArea area, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var container = area switch
        {
            BlobArea.Staging => _options.StagingContainer,
            BlobArea.Renditions => _options.RenditionsContainer,
            BlobArea.Downloads => _options.DownloadsContainer,
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown blob area."),
        };

        return _service.GetBlobContainerClient(container).GetBlobClient(name);
    }
}
