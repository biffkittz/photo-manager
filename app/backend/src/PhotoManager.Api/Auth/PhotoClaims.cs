using System.Globalization;
using System.Security.Claims;

namespace PhotoManager.Api.Auth;

/// <summary>Claim types and accessors for the signed-in user.</summary>
public static class PhotoClaims
{
    /// <summary>GitHub display name.</summary>
    public const string DisplayName = "urn:github:name";

    /// <summary>GitHub avatar URL.</summary>
    public const string AvatarUrl = "urn:github:avatar";

    /// <summary>When the user signed in with GitHub (Unix seconds); bounds the absolute session lifetime.</summary>
    public const string SignedInAt = "urn:photo-manager:signed-in-at";

    /// <summary>Gets the numeric GitHub id, or 0 when absent.</summary>
    /// <param name="user">The principal.</param>
    /// <returns>The id.</returns>
    public static long GitHubId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
    }

    /// <summary>Gets when the user signed in with GitHub, or null when the session predates the claim.</summary>
    /// <param name="user">The principal.</param>
    /// <returns>The sign-in time.</returns>
    public static DateTimeOffset? SignedInAtUtc(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return long.TryParse(user.FindFirstValue(SignedInAt), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    /// <summary>Gets the GitHub login (used as the actor in audit records).</summary>
    /// <param name="user">The principal.</param>
    /// <returns>The login, or "anonymous".</returns>
    public static string Login(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    }
}
