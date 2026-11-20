using System.Security.Cryptography;
using PhotoManager.Core.Crypto;

namespace PhotoManager.Core.Tests.Crypto;

/// <summary>Key-type isolation, key-cache bounds and stream ownership (regressions from the 2026-11-19 review).</summary>
public sealed class KeyIsolationTests
{
    private const int TestIterations = 100_000;
    private static readonly byte[] ArchiveSalt = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unexpected_key_type_is_rejected_before_any_key_is_resolved()
    {
        // A worker reading web-key data (which the internet-facing tier can write) must never run a password
        // derivation for whatever the header claims.
        using var ring = new PhotoKeyRing("archive password", [RandomNumberGenerator.GetBytes(32)], ArchiveSalt, TestIterations);
        var archiveFile = await EncryptAsync(ring.ActiveArchiveKey);
        var resolver = new CountingResolver(ring);

        await Assert.ThrowsAsync<PhotoDecryptionException>(() =>
            PhotoCipherV2.OpenDecryptStreamAsync(new MemoryStream(archiveFile), resolver, expectedKeyType: V2KeyType.RawKey, cancellationToken: Ct));
        await Assert.ThrowsAsync<PhotoDecryptionException>(() =>
            PhotoCipherV2.ReadMetadataAsync(new MemoryStream(archiveFile), resolver, V2KeyType.RawKey, Ct));
        Assert.Equal(0, resolver.Calls);

        await using var decrypted = await PhotoCipherV2.OpenDecryptStreamAsync(new MemoryStream(archiveFile), resolver, expectedKeyType: V2KeyType.Password, cancellationToken: Ct);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public void Password_keys_that_do_not_match_the_header_are_never_cached()
    {
        using var ring = new PhotoKeyRing("archive password", [], ArchiveSalt, TestIterations);
        var archiveKey = ring.ActiveArchiveKey;
        for (var i = 0; i < 12; i++)
        {
            Assert.Null(ring.Resolve(V2KeyType.Password, RandomNumberGenerator.GetBytes(8), TestIterations, RandomNumberGenerator.GetBytes(16)));
        }

        Assert.Equal(1, ring.CachedPasswordKeyCount);
        Assert.Same(archiveKey, ring.Resolve(V2KeyType.Password, archiveKey.KeyId, TestIterations, ArchiveSalt));
    }

    [Fact]
    public async Task Failed_opens_dispose_the_stream_they_took_ownership_of()
    {
        using var ring = new PhotoKeyRing("archive password", [RandomNumberGenerator.GetBytes(32)], ArchiveSalt, TestIterations);

        var legacy = new TrackingStream(new byte[20]);
        await Assert.ThrowsAsync<PhotoDecryptionException>(() => LegacyPhotoCipher.OpenDecryptStreamAsync(legacy, "password", Ct));
        Assert.True(legacy.Disposed);

        var owned = new TrackingStream(new byte[10]);
        await Assert.ThrowsAsync<PhotoDecryptionException>(() => PhotoCipherV2.OpenDecryptStreamAsync(owned, ring, cancellationToken: Ct));
        Assert.True(owned.Disposed);

        var borrowed = new TrackingStream(new byte[10]);
        await Assert.ThrowsAsync<PhotoDecryptionException>(() => PhotoCipherV2.OpenDecryptStreamAsync(borrowed, ring, leaveOpen: true, cancellationToken: Ct));
        Assert.False(borrowed.Disposed);
    }

    private static async Task<byte[]> EncryptAsync(V2MasterKey key)
    {
        using var output = new MemoryStream();
        await PhotoCipherV2.EncryptAsync(new MemoryStream(RandomNumberGenerator.GetBytes(100)), output, key, cancellationToken: Ct);
        return output.ToArray();
    }

    private sealed class CountingResolver(IV2KeyResolver inner) : IV2KeyResolver
    {
        public int Calls { get; private set; }

        public V2MasterKey? Resolve(V2KeyType keyType, ReadOnlySpan<byte> keyId, int kdfIterations, ReadOnlySpan<byte> kdfSalt)
        {
            Calls++;
            return inner.Resolve(keyType, keyId, kdfIterations, kdfSalt);
        }
    }

    private sealed class TrackingStream(byte[] data) : MemoryStream(data)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
