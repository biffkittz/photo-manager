using System.Buffers.Binary;
using System.Security.Cryptography;
using PhotoManager.Core.Crypto;

namespace PhotoManager.Core.Tests.Crypto;

public sealed class PhotoCipherV2Tests
{
    private const int SmallChunk = 1024;
    private const int TestIterations = 100_000;
    private static readonly byte[] ArchiveSalt = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<int> Sizes => [0, 1, SmallChunk - 1, SmallChunk, SmallChunk + 1, (3 * SmallChunk) + 5, 4 * SmallChunk];

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task Round_trips_with_a_raw_web_key(int size)
    {
        using var ring = WebOnlyRing(out _);
        var data = RandomNumberGenerator.GetBytes(size);

        var encrypted = await EncryptAsync(data, ring.ActiveWebKey);
        var (plaintext, metadata) = await DecryptAsync(encrypted, ring);

        Assert.Equal(data, plaintext);
        Assert.Null(metadata);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public async Task Round_trips_with_a_password_derived_archive_key(int size)
    {
        using var ring = new PhotoKeyRing("archive password", [], ArchiveSalt, TestIterations);
        var data = RandomNumberGenerator.GetBytes(size);

        var encrypted = await EncryptAsync(data, ring.ActiveArchiveKey);
        var (plaintext, _) = await DecryptAsync(encrypted, ring);

        Assert.Equal(data, plaintext);
    }

    [Fact]
    public async Task Writes_the_documented_header_layout()
    {
        using var ring = WebOnlyRing(out _);
        var encrypted = await EncryptAsync([1, 2, 3], ring.ActiveWebKey);

        Assert.True(PhotoCipherV2.HasV2Magic(encrypted));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4D, 0x56, 0x32, 0x0D, 0x0A, 0x1A }, encrypted[..8]);
        Assert.Equal(2, encrypted[8]);
        Assert.Equal((byte)V2KeyType.RawKey, encrypted[9]);
        Assert.Equal(SmallChunk, BinaryPrimitives.ReadInt32BigEndian(encrypted.AsSpan(12)));
        Assert.Equal(ring.ActiveWebKey.KeyId.ToArray(), encrypted[20..28]);
        // header(80) + one final chunk: 3 bytes + 16-byte tag
        Assert.Equal(80 + 3 + 16, encrypted.Length);
    }

    [Fact]
    public async Task Seals_and_authenticates_embedded_metadata()
    {
        using var ring = WebOnlyRing(out _);
        var metadata = new EmbeddedPhotoMetadata
        {
            PhotoId = Guid.NewGuid(),
            OriginalFileName = "beach day.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 3,
            Description = "woman and boy smiling on the beach",
            Tags = ["beach", "family"],
            UploadedAt = DateTimeOffset.UnixEpoch,
        };

        var encrypted = await EncryptAsync([1, 2, 3], ring.ActiveWebKey, metadata);
        var (_, roundTripped) = await DecryptAsync(encrypted, ring);
        var headerOnly = await PhotoCipherV2.ReadMetadataAsync(new MemoryStream(encrypted), ring, cancellationToken: Ct);

        Assert.NotNull(roundTripped);
        Assert.Equal(metadata.PhotoId, roundTripped.PhotoId);
        Assert.Equal(metadata.Description, roundTripped.Description);
        Assert.Equal(metadata.Tags, roundTripped.Tags);
        Assert.Equal(metadata.PhotoId, headerOnly!.PhotoId);
        // The plaintext description must not appear anywhere in the ciphertext.
        Assert.DoesNotContain("smiling", System.Text.Encoding.UTF8.GetString(encrypted), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(12)] // chunk size field
    [InlineData(25)] // key id (changes which key is looked up)
    [InlineData(50)] // file salt
    [InlineData(79)] // metadata length
    [InlineData(81)] // inside the metadata block
    [InlineData(-1)] // last byte of the last chunk (tag)
    [InlineData(-200)] // inside a content chunk
    public async Task Detects_tampering_anywhere_in_the_file(int offset)
    {
        using var ring = WebOnlyRing(out _);
        var metadata = new EmbeddedPhotoMetadata { PhotoId = Guid.NewGuid(), OriginalFileName = "a.jpg", ContentType = "image/jpeg" };
        var encrypted = await EncryptAsync(RandomNumberGenerator.GetBytes(3 * SmallChunk), ring.ActiveWebKey, metadata);

        var index = offset >= 0 ? offset : encrypted.Length + offset;
        encrypted[index] ^= 0x01;

        var error = await Assert.ThrowsAnyAsync<Exception>(() => DecryptAsync(encrypted, ring));
        Assert.True(error is PhotoDecryptionException or PhotoKeyUnavailableException, error.GetType().Name);
    }

    [Fact]
    public async Task Detects_truncation_at_a_chunk_boundary()
    {
        using var ring = WebOnlyRing(out _);
        var encrypted = await EncryptAsync(RandomNumberGenerator.GetBytes(3 * SmallChunk), ring.ActiveWebKey);

        // Drop the final chunk entirely: the remaining last chunk was sealed as non-final.
        var truncated = encrypted[..(encrypted.Length - (SmallChunk + 16))];

        await Assert.ThrowsAsync<PhotoDecryptionException>(() => DecryptAsync(truncated, ring));
    }

    [Fact]
    public async Task Detects_appended_data()
    {
        using var ring = WebOnlyRing(out _);
        var encrypted = await EncryptAsync(RandomNumberGenerator.GetBytes(SmallChunk), ring.ActiveWebKey);

        await Assert.ThrowsAsync<PhotoDecryptionException>(() => DecryptAsync([.. encrypted, .. new byte[40]], ring));
    }

    [Fact]
    public async Task Detects_reordered_chunks()
    {
        using var ring = WebOnlyRing(out _);
        var encrypted = await EncryptAsync(RandomNumberGenerator.GetBytes((3 * SmallChunk) + 10), ring.ActiveWebKey);
        var unit = SmallChunk + 16;
        var first = encrypted.AsSpan(80, unit).ToArray();
        encrypted.AsSpan(80 + unit, unit).CopyTo(encrypted.AsSpan(80, unit));
        first.CopyTo(encrypted.AsSpan(80 + unit, unit));

        await Assert.ThrowsAsync<PhotoDecryptionException>(() => DecryptAsync(encrypted, ring));
    }

    [Fact]
    public async Task Unknown_key_is_reported_as_unavailable()
    {
        using var writer = WebOnlyRing(out _);
        using var reader = WebOnlyRing(out _);
        var encrypted = await EncryptAsync([1, 2, 3], writer.ActiveWebKey);

        await Assert.ThrowsAsync<PhotoKeyUnavailableException>(() => DecryptAsync(encrypted, reader));
    }

    [Fact]
    public async Task Web_tier_ring_cannot_open_archive_files()
    {
        using var workerRing = new PhotoKeyRing("archive password", [RandomNumberGenerator.GetBytes(32)], ArchiveSalt, TestIterations);
        var encrypted = await EncryptAsync([1, 2, 3], workerRing.ActiveArchiveKey);
        using var webRing = WebOnlyRing(out _);

        Assert.False(webRing.CanAccessArchive);
        Assert.Throws<PhotoKeyUnavailableException>(() => webRing.ActiveArchiveKey);
        Assert.Throws<PhotoKeyUnavailableException>(webRing.GetLegacyArchivePassword);
        await Assert.ThrowsAsync<PhotoKeyUnavailableException>(() => DecryptAsync(encrypted, webRing));
    }

    [Fact]
    public async Task Rotated_web_keys_still_decrypt_older_files()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        using var oldRing = new PhotoKeyRing(null, [oldKey], ArchiveSalt, TestIterations);
        var encrypted = await EncryptAsync([9, 8, 7], oldRing.ActiveWebKey);

        using var rotatedRing = new PhotoKeyRing(null, [RandomNumberGenerator.GetBytes(32), oldKey], ArchiveSalt, TestIterations);
        var (plaintext, _) = await DecryptAsync(encrypted, rotatedRing);

        Assert.Equal(new byte[] { 9, 8, 7 }, plaintext);
        Assert.NotEqual(oldRing.ActiveWebKey.KeyIdHex, rotatedRing.ActiveWebKey.KeyIdHex);
    }

    [Fact]
    public async Task Disposing_without_completing_leaves_an_unreadable_file()
    {
        using var ring = WebOnlyRing(out _);
        using var output = new MemoryStream();
        await using (var encryptor = await PhotoCipherV2.CreateEncryptStreamAsync(output, ring.ActiveWebKey, chunkSize: SmallChunk, leaveOpen: true, cancellationToken: Ct))
        {
            await encryptor.WriteAsync(RandomNumberGenerator.GetBytes((2 * SmallChunk) + 1), Ct);
            // Simulate a source failure: no CompleteAsync().
        }

        await Assert.ThrowsAsync<PhotoDecryptionException>(() => DecryptAsync(output.ToArray(), ring));
    }

    [Fact]
    public void Header_parser_rejects_hostile_kdf_parameters()
    {
        var header = new byte[80];
        new byte[] { 0x89, 0x50, 0x4D, 0x56, 0x32, 0x0D, 0x0A, 0x1A }.CopyTo(header, 0);
        header[8] = 2;
        header[9] = (byte)V2KeyType.Password;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), SmallChunk);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), 50_000_000); // would take minutes to derive

        Assert.Throws<PhotoDecryptionException>(() => V2Header.Parse(header));
    }

    [Fact]
    public async Task Synchronous_reads_match_asynchronous_reads()
    {
        using var ring = WebOnlyRing(out _);
        var data = RandomNumberGenerator.GetBytes((2 * SmallChunk) + 77);
        var encrypted = await EncryptAsync(data, ring.ActiveWebKey);

        await using var decryptor = await PhotoCipherV2.OpenDecryptStreamAsync(new MemoryStream(encrypted), ring, cancellationToken: Ct);
        using var actual = new MemoryStream();
#pragma warning disable CA1849 // exercising the synchronous Read path on purpose
        decryptor.CopyTo(actual, 333);
#pragma warning restore CA1849

        Assert.Equal(data, actual.ToArray());
    }

    private static PhotoKeyRing WebOnlyRing(out byte[] webKey)
    {
        webKey = RandomNumberGenerator.GetBytes(32);
        return new PhotoKeyRing(null, [webKey], ArchiveSalt, TestIterations);
    }

    private static async Task<byte[]> EncryptAsync(byte[] data, V2MasterKey key, EmbeddedPhotoMetadata? metadata = null)
    {
        using var output = new MemoryStream();
        await using (var encryptor = await PhotoCipherV2.CreateEncryptStreamAsync(output, key, metadata, SmallChunk, leaveOpen: true, cancellationToken: Ct))
        {
            // Write in odd-sized pieces to exercise chunk-boundary handling.
            for (var offset = 0; offset < data.Length; offset += 100)
            {
                await encryptor.WriteAsync(data.AsMemory(offset, Math.Min(100, data.Length - offset)), Ct);
            }

            await encryptor.CompleteAsync(Ct);
        }

        return output.ToArray();
    }

    private static async Task<(byte[] Plaintext, EmbeddedPhotoMetadata? Metadata)> DecryptAsync(byte[] encrypted, IV2KeyResolver keys)
    {
        using var output = new MemoryStream();
        var metadata = await PhotoCipherV2.DecryptAsync(new MemoryStream(encrypted), output, keys, Ct);
        return (output.ToArray(), metadata);
    }
}
