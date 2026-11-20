using System.Globalization;
using System.Security.Claims;

namespace PhotoManager.Api.Auth;

/// <summary>Claim types and accessors for the signed-in user.</summary>
/// <remarks>
/// Two kinds of accounts sign in. Admins (and any member listed in configuration) use GitHub; their user id is their
/// numeric GitHub id, which is positive. People invited from the People page use Google; their user id is their
/// <c>family_members</c> id, which is negative. The user id is what ownership (uploads, downloads) and the audit trail
/// record, so the two can never collide.
/// </remarks>
public static class PhotoClaims
{
    /// <summary>Display name.</summary>
    public const string DisplayName = "urn:github:name";

    /// <summary>GitHub avatar URL (Google members have none: the content security policy only allows GitHub avatars).</summary>
    public const string AvatarUrl = "urn:github:avatar";

    /// <summary>When the user signed in (Unix seconds); bounds the absolute session lifetime.</summary>
    public const string SignedInAt = "urn:photo-manager:signed-in-at";

    /// <summary>Which provider the user signed in with (<see cref="SignInProviders"/>).</summary>
    public const string Provider = "urn:photo-manager:provider";

    /// <summary>The Google account (<c>sub</c>) a member's session was signed in with; the session ends if the member's link changes.</summary>
    public const string GoogleSubject = "urn:photo-manager:google-subject";

    /// <summary>Set (to <c>true</c>) while signing in with Google when Google reports the email address as verified.</summary>
    public const string GoogleEmailVerified = "urn:photo-manager:google-email-verified";

    /// <summary>Gets the user id (positive: GitHub id; negative: invited member), or 0 when absent.</summary>
    /// <param name="user">The principal.</param>
    /// <returns>The id.</returns>
    public static long UserId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) ? id : 0;
    }

    /// <summary>Gets the sign-in provider. Sessions from before the claim existed are GitHub sessions.</summary>
    /// <param name="user">The principal.</param>
    /// <returns><see cref="SignInProviders.GitHub"/> or <see cref="SignInProviders.Google"/>.</returns>
    public static string SignInProvider(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindFirstValue(Provider) ?? SignInProviders.GitHub;
    }

    /// <summary>Gets when the user signed in, or null when the session predates the claim.</summary>
    /// <param name="user">The principal.</param>
    /// <returns>The sign-in time.</returns>
    public static DateTimeOffset? SignedInAtUtc(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return long.TryParse(user.FindFirstValue(SignedInAt), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    /// <summary>Gets the name recorded as the actor in audit records and as the uploader: the GitHub login, or a member's display name.</summary>
    /// <param name="user">The principal.</param>
    /// <returns>The name, or "anonymous".</returns>
    public static string Login(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    }
}
