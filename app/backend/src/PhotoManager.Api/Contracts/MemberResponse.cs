namespace PhotoManager.Api.Contracts;

/// <summary>A person invited from the People page.</summary>
/// <param name="Id">Member id (negative; see <c>PhotoClaims.UserId</c>).</param>
/// <param name="Name">The name the admin gave.</param>
/// <param name="Status"><c>Invited</c>, <c>Expired</c>, <c>Active</c> or <c>Removed</c>.</param>
/// <param name="GoogleEmail">Email of the linked Google account, as Google last reported it.</param>
/// <param name="InviteEmail">The only Google address that may accept the invite, when limited to one.</param>
/// <param name="InvitedBy">Who sent the latest invite.</param>
/// <param name="InviteExpiresAt">When the pending invite link stops working.</param>
/// <param name="JoinedAt">When the person accepted.</param>
/// <param name="LastSignInAt">When the person last signed in.</param>
/// <param name="RemovedAt">When the person was removed.</param>
public sealed record MemberResponse(
    long Id,
    string Name,
    string Status,
    string? GoogleEmail,
    string? InviteEmail,
    string InvitedBy,
    DateTimeOffset? InviteExpiresAt,
    DateTimeOffset? JoinedAt,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset? RemovedAt);
