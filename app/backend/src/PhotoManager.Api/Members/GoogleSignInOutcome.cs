namespace PhotoManager.Api.Members;

/// <summary>What happened when someone came back from Google.</summary>
public enum GoogleSignInOutcome
{
    /// <summary>A current member signed in (or accepted their invite).</summary>
    SignedIn,

    /// <summary>The Google account is not linked to any current member, and no invite was being accepted.</summary>
    NotInvited,

    /// <summary>The invite expired, was used, was replaced by a newer link, or the person was removed.</summary>
    InviteInvalid,

    /// <summary>The invite is limited to another Google address (or Google has not verified this one).</summary>
    WrongAccount,

    /// <summary>The Google account already belongs to another current member.</summary>
    AccountInUse,
}
