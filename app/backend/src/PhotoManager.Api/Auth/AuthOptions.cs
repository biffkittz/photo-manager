namespace PhotoManager.Api.Auth;

/// <summary>Sign-in settings. Section <c>Auth</c>.</summary>
public sealed class AuthOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Auth";

    /// <summary>Gets the allowlist. Anyone else with a GitHub account is refused, even after authenticating with GitHub.</summary>
    public List<AllowedUser> Users { get; } = [];

    /// <summary>Gets or sets the sliding session lifetime (idle timeout).</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// Gets or sets the absolute session lifetime: after this long since signing in, a new GitHub sign-in is required
    /// no matter how active the session is (sliding renewal alone would let an open tab, or a stolen cookie that is
    /// replayed regularly, live forever).
    /// </summary>
    public TimeSpan MaxSessionLifetime { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Gets or sets the GitHub OAuth app client id (Key Vault secret <c>Auth--GitHubClientId</c>).</summary>
    public string? GitHubClientId { get; set; }

    /// <summary>Gets or sets the GitHub OAuth app client secret (Key Vault secret <c>Auth--GitHubClientSecret</c>).</summary>
    public string? GitHubClientSecret { get; set; }

    /// <summary>Finds the allowlist entry for a GitHub user id.</summary>
    /// <param name="gitHubId">Numeric id.</param>
    /// <returns>The entry, or null when the user is not allowed.</returns>
    public AllowedUser? Find(long gitHubId) => Users.FirstOrDefault(u => u.GitHubId == gitHubId && u.GitHubId > 0);
}
