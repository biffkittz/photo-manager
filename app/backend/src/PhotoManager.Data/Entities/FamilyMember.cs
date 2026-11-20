namespace PhotoManager.Data.Entities;

/// <summary>
/// A person an admin invited from the People page, who signs in with Google. Admins are not stored here: they are
/// the GitHub accounts in configuration (Terraform <c>github_users</c>), so nothing in the database can create or
/// remove an admin.
/// </summary>
public sealed class FamilyMember : ITimestamped
{
    /// <summary>
    /// Gets or sets the member's user id, used wherever the app records who did something (uploads, downloads,
    /// audit). Always negative, so it can never collide with a GitHub id (positive) in those columns.
    /// </summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the name the admin gave when inviting (shown in the app and the audit trail).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the Google account's permanent id (<c>sub</c>), bound when the invite is accepted.</summary>
    public string? GoogleSubject { get; set; }

    /// <summary>Gets or sets the Google account's email address at the last sign-in (display only, never used for access).</summary>
    public string? GoogleEmail { get; set; }

    /// <summary>
    /// Gets or sets the only Google address that may accept the invite (lower case), when the admin gave one; the
    /// Google account must report it as verified. Null: whoever opens the link first can accept it.
    /// </summary>
    public string? InviteEmail { get; set; }

    /// <summary>Gets or sets the SHA-256 (hex) of the pending invite token; null once used or revoked. The token itself is never stored.</summary>
    public string? InviteTokenHash { get; set; }

    /// <summary>Gets or sets when the pending invite stops working.</summary>
    public DateTimeOffset? InviteExpiresAt { get; set; }

    /// <summary>Gets or sets the user id of the admin who sent the latest invite.</summary>
    public long InvitedById { get; set; }

    /// <summary>Gets or sets the display name of the admin who sent the latest invite.</summary>
    public string InvitedBy { get; set; } = string.Empty;

    /// <summary>Gets or sets when the member accepted the invite (bound a Google account).</summary>
    public DateTimeOffset? JoinedAt { get; set; }

    /// <summary>Gets or sets when the member last signed in.</summary>
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>Gets or sets when an admin removed the member (or revoked the invite); null while they have access.</summary>
    public DateTimeOffset? RemovedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token.</summary>
    public uint Version { get; set; }
}
