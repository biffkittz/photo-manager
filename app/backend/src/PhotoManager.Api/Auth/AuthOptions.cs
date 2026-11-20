namespace PhotoManager.Api.Auth;

/// <summary>Sign-in settings. Section <c>Auth</c>.</summary>
public sealed class AuthOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// Gets the GitHub allowlist: the admins (and any member managed in Terraform). Anyone else with a GitHub account
    /// is refused, even after authenticating with GitHub. Family members invited from the People page sign in with
    /// Google instead and are stored in the database; nothing there can create an admin.
    /// </summary>
    public List<AllowedUser> Users { get; } = [];

    /// <summary>Gets or sets the sliding session lifetime (idle timeout).</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// Gets or sets the absolute session lifetime: after this long since signing in, a new sign-in is required
    /// no matter how active the session is (sliding renewal alone would let an open tab, or a stolen cookie that is
    /// replayed regularly, live forever).
    /// </summary>
    public TimeSpan MaxSessionLifetime { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Gets or sets the GitHub OAuth app client id (Key Vault secret <c>Auth--GitHubClientId</c>).</summary>
    public string? GitHubClientId { get; set; }

    /// <summary>Gets or sets the GitHub OAuth app client secret (Key Vault secret <c>Auth--GitHubClientSecret</c>).</summary>
    public string? GitHubClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the Google OAuth client id (Key Vault secret <c>Auth--GoogleClientId</c>). Optional: without it
    /// (and the secret) Google sign-in and invitations are switched off.
    /// </summary>
    public string? GoogleClientId { get; set; }

    /// <summary>Gets or sets the Google OAuth client secret (Key Vault secret <c>Auth--GoogleClientSecret</c>).</summary>
    public string? GoogleClientSecret { get; set; }

    /// <summary>Gets a value indicating whether Google sign-in (and so inviting family members) is configured.</summary>
    public bool GoogleSignInEnabled => !string.IsNullOrWhiteSpace(GoogleClientId) && !string.IsNullOrWhiteSpace(GoogleClientSecret);

    /// <summary>Finds the allowlist entry for a GitHub user id.</summary>
    /// <param name="gitHubId">Numeric id.</param>
    /// <returns>The entry, or null when the user is not allowed.</returns>
    public AllowedUser? Find(long gitHubId) => Users.FirstOrDefault(u => u.GitHubId == gitHubId && u.GitHubId > 0);
}
