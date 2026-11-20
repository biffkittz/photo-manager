namespace PhotoManager.Api.Members;

/// <summary>Where an invited person stands, as shown on the People page.</summary>
public static class MemberStatus
{
    /// <summary>Invite link sent and still valid.</summary>
    public const string Invited = "Invited";

    /// <summary>Invite link expired before it was used.</summary>
    public const string Expired = "Expired";

    /// <summary>Linked to a Google account and allowed to sign in.</summary>
    public const string Active = "Active";

    /// <summary>Removed by an admin (or the invite was cancelled).</summary>
    public const string Removed = "Removed";
}
