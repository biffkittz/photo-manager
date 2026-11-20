using System.Security.Cryptography;
using System.Text;
using PhotoManager.Infrastructure.Configuration;
using PhotoManager.Infrastructure.Gemini;

namespace PhotoManager.Worker.Tests.Infrastructure;

public sealed class ConfigurationTests
{
    [Fact]
    public void Default_archive_kdf_salt_matches_its_documented_derivation()
    {
        // docs/encryption.md: first 16 bytes of SHA-256("photo-manager/archive-kdf-salt/v1").
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes("photo-manager/archive-kdf-salt/v1"))[..16];
        Assert.Equal(CryptoOptions.DefaultArchiveKdfSalt, Convert.ToBase64String(expected));
    }

    [Theory]
    [InlineData("  \"Woman and boy smiling on the beach.\"  ", "Woman and boy smiling on the beach")]
    [InlineData("line one\nline two\u0000‮", "line one line two")]
    [InlineData("", "")]
    public void Sanitizes_model_descriptions(string raw, string expected)
    {
        Assert.Equal(expected, DescriptionSanitizer.Description(raw));
    }

    [Fact]
    public void Caps_description_length_at_a_word_boundary()
    {
        var sanitized = DescriptionSanitizer.Description(string.Join(' ', Enumerable.Repeat("word", 200)));
        Assert.True(sanitized.Length <= DescriptionSanitizer.MaxDescriptionLength);
        Assert.EndsWith("word", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalizes_and_limits_tags()
    {
        var tags = DescriptionSanitizer.Tags(["Beach", "beach", "<script>", "sun-set", "", null, .. Enumerable.Range(0, 20).Select(i => $"t{i}")]);
        Assert.Equal(["beach", "script", "sun-set", "t0", "t1", "t2", "t3", "t4", "t5", "t6"], tags);
    }
}
