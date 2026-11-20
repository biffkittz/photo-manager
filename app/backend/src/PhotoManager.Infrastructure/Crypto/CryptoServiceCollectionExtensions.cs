using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PhotoManager.Core.Crypto;
using PhotoManager.Infrastructure.Configuration;

namespace PhotoManager.Infrastructure.Crypto;

/// <summary>Registers the key ring and <see cref="PhotoCryptoService"/>.</summary>
public static class CryptoServiceCollectionExtensions
{
    /// <summary>Registers crypto services with fail-fast validation of every configured key.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPhotoCrypto(this IServiceCollection services)
    {
        services.AddOptions<CryptoOptions>()
            .BindConfiguration(CryptoOptions.SectionName)
            .Validate(o => IsKey(o.WebKey, 32), "Crypto:WebKey must be the base64 encoding of 32 random bytes.")
            .Validate(o => string.IsNullOrEmpty(o.WebKeyPrevious) || IsKey(o.WebKeyPrevious, 32), "Crypto:WebKeyPrevious must be base64 of 32 bytes.")
            .Validate(o => IsKey(o.ArchiveKdfSalt, 16), "Crypto:ArchiveKdfSalt must be base64 of 16 bytes.")
            .Validate(o => o.ArchiveKdfIterations is >= 100_000 and <= 2_000_000, "Crypto:ArchiveKdfIterations must be between 100,000 and 2,000,000.")
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CryptoOptions>>().Value;
            var webKeys = new List<byte[]> { Convert.FromBase64String(options.WebKey!) };
            if (!string.IsNullOrEmpty(options.WebKeyPrevious))
            {
                webKeys.Add(Convert.FromBase64String(options.WebKeyPrevious));
            }

            return new PhotoKeyRing(
                options.ArchivePassword,
                webKeys,
                Convert.FromBase64String(options.ArchiveKdfSalt),
                options.ArchiveKdfIterations);
        });
        services.AddSingleton<PhotoCryptoService>();
        return services;
    }

    /// <summary>Returns true when <paramref name="value"/> is base64 of exactly <paramref name="length"/> bytes.</summary>
    /// <param name="value">Candidate base64 string.</param>
    /// <param name="length">Required decoded length.</param>
    /// <returns>Whether the value is valid.</returns>
    internal static bool IsKey(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[64];
        return Convert.TryFromBase64String(value, buffer, out var written) && written == length;
    }
}
