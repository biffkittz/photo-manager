using System.Security.Cryptography;
using PhotoManager.Core.Crypto;

namespace PhotoManager.Core.Tests.Crypto;

/// <summary>
/// Compatibility tests against fixtures produced by the real protect.ps1 code
/// (see TestData/Legacy/generate-vectors.ps1).
/// </summary>
public sealed class LegacyPhotoCipherTests
{
    // Test-only password used by generate-vectors.ps1. Includes non-ASCII characters to prove the
    // UTF-8 password encoding matches .NET's Rfc2898DeriveBytes(string, byte[], int) constructor.
    private const string TestPassword = "test-only pässwörd ✓ 123";

    private static readonly string VectorDirectory = Path.Combine(AppContext.BaseDirectory, "TestData", "Legacy");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1000)]
    [InlineData(65539)]
    public async Task Decrypts_files_written_by_protect_ps1(int size)
    {
        var expected = await File.ReadAllBytesAsync(Path.Combine(VectorDirectory, $"plain-{size}.bin"), TestContext.Current.CancellationToken);
        await using var encrypted = File.OpenRead(Path.Combine(VectorDirectory, $"plain-{size}.bin.encrypted"));

        await using var plaintext = await LegacyPhotoCipher.OpenDecryptStreamAsync(encrypted, TestPassword, TestContext.Current.CancellationToken);
        using var actual = new MemoryStream();
        await plaintext.CopyToAsync(actual, TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual.ToArray());
    }

    [Fact]
    public async Task Wrong_password_fails_on_padding()
    {
        await using var encrypted = File.OpenRead(Path.Combine(VectorDirectory, "plain-1000.bin.encrypted"));
        await using var plaintext = await LegacyPhotoCipher.OpenDecryptStreamAsync(encrypted, "not the password", TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => plaintext.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(4096)]
    public async Task Round_trips_with_the_protect_ps1_layout(int size)
    {
        var data = RandomNumberGenerator.GetBytes(size);
        using var encrypted = new MemoryStream();
        await LegacyPhotoCipher.EncryptAsync(new MemoryStream(data), encrypted, TestPassword, TestContext.Current.CancellationToken);

        // salt(32) + iv(16) + PKCS7-padded ciphertext (always at least one block).
        Assert.Equal(LegacyPhotoCipher.HeaderSize + ((size / 16) + 1) * 16, encrypted.Length);

        encrypted.Position = 0;
        await using var plaintext = await LegacyPhotoCipher.OpenDecryptStreamAsync(encrypted, TestPassword, TestContext.Current.CancellationToken);
        using var actual = new MemoryStream();
        await plaintext.CopyToAsync(actual, TestContext.Current.CancellationToken);
        Assert.Equal(data, actual.ToArray());
    }

    [Fact]
    public async Task Rejects_input_shorter_than_the_header()
    {
        await Assert.ThrowsAsync<PhotoDecryptionException>(
            () => LegacyPhotoCipher.OpenDecryptStreamAsync(new MemoryStream(new byte[20]), TestPassword, TestContext.Current.CancellationToken));
    }
}
