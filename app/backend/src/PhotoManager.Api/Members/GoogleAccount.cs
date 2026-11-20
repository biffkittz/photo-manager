namespace PhotoManager.Api.Members;

/// <summary>The Google account someone signed in with, as Google reported it.</summary>
/// <param name="Subject">Google's permanent account id (<c>sub</c>); the only value used to recognize a member.</param>
/// <param name="Email">The account's email address (shown to admins; checked only for invites limited to one address).</param>
/// <param name="EmailVerified">Whether Google has verified that address.</param>
public sealed record GoogleAccount(string Subject, string? Email, bool EmailVerified);
