using System.Collections.Concurrent;
using PhotoManager.Infrastructure.Storage;

namespace PhotoManager.Api.Tests.Support;

/// <summary>In-memory <see cref="IBlobStore"/> so API tests need no Azurite.</summary>
public sealed class InMemoryBlobStore : IBlobStore
{
    public ConcurrentDictionary<(BlobArea Area, string Name), byte[]> Blobs { get; } = new();

    public Task<Stream> OpenReadAsync(BlobArea area, string name, CancellationToken cancellationToken) =>
        Blobs.TryGetValue((area, name), out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
            : throw new StorageObjectNotFoundException($"{area}/{name}");

    public Task<Stream> OpenWriteAsync(BlobArea area, string name, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new CommitOnDisposeStream(bytes => Blobs[(area, name)] = bytes));

    public Task UploadAsync(BlobArea area, string name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        Blobs[(area, name)] = content.ToArray();
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(BlobArea area, string name, CancellationToken cancellationToken) =>
        Task.FromResult(Blobs.ContainsKey((area, name)));

    public Task DeleteIfExistsAsync(BlobArea area, string name, CancellationToken cancellationToken)
    {
        Blobs.TryRemove((area, name), out _);
        return Task.CompletedTask;
    }

    private sealed class CommitOnDisposeStream(Action<byte[]> commit) : MemoryStream
    {
        private bool _committed;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_committed)
            {
                _committed = true;
                commit(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}
