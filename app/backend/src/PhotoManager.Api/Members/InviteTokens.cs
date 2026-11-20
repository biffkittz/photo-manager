using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace PhotoManager.Api.Members;

/// <summary>
/// Invite link tokens: 256 random bits, shown to the admin once and stored only as a SHA-256 hash, so a copy of the
/// database (or a backup) cannot be turned into working invite links.
/// </summary>
internal static class InviteTokens
{
    /// <summary>Length of a token: 32 bytes in unpadded base64url.</summary>
    public const int Length = 43;

    /// <summary>Creates a new random token.</summary>
    /// <returns>The token (URL-safe).</returns>
    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Checks the shape of a token before it is looked up.</summary>
    /// <param name="token">Untrusted input.</param>
    /// <returns>Whether it could be a token.</returns>
    public static bool IsWellFormed(string? token) =>
        token is { Length: Length } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Hashes a token for storage and lookup.</summary>
    /// <param name="token">The token.</param>
    /// <returns>Lower-case hex SHA-256.</returns>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
