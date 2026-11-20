using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace PhotoManager.Api.Members;

/// <summary>
/// An invite someone opened and is accepting by signing in with Google. It travels from the invite page to the
/// Google callback in a protected cookie and then inside the (encrypted) OAuth state, and is checked against the
/// database again when the person comes back from Google.
/// </summary>
/// <param name="MemberId">The invited member.</param>
/// <param name="TokenHash">Hash of the invite token that was opened; a newer link for the same person replaces it.</param>
internal sealed record PendingInvite(long MemberId, string TokenHash)
{
    private const string MemberKey = ".photo-manager.invite.member";
    private const string HashKey = ".photo-manager.invite.hash";

    /// <summary>Adds the invite to the properties of a Google challenge.</summary>
    /// <param name="properties">The challenge properties (protected in the OAuth state parameter).</param>
    public void WriteTo(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        properties.Items[MemberKey] = MemberId.ToString(CultureInfo.InvariantCulture);
        properties.Items[HashKey] = TokenHash;
    }

    /// <summary>Reads the invite carried by a Google sign-in, if any.</summary>
    /// <param name="properties">The properties returned with the ticket.</param>
    /// <returns>The invite, or null for an ordinary sign-in.</returns>
    public static PendingInvite? ReadFrom(AuthenticationProperties? properties) =>
        properties is not null
        && properties.Items.TryGetValue(MemberKey, out var member)
        && long.TryParse(member, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var memberId)
        && properties.Items.TryGetValue(HashKey, out var hash)
        && !string.IsNullOrEmpty(hash)
            ? new PendingInvite(memberId, hash)
            : null;
}
