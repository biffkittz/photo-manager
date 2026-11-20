namespace PhotoManager.Api.Auth;

/// <summary>Application roles and authorization policy names.</summary>
public static class PhotoRoles
{
    /// <summary>Can browse, upload, download and edit descriptions.</summary>
    public const string Member = "Member";

    /// <summary>Member rights plus maintenance actions (re-index, backfills).</summary>
    public const string Admin = "Admin";

    /// <summary>Policy requiring the <see cref="Admin"/> role.</summary>
    public const string AdminPolicy = "AdminOnly";
}
