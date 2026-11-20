namespace PhotoManager.Api.Auth;

/// <summary>One GitHub account allowed to sign in.</summary>
public sealed class AllowedUser
{
    /// <summary>
    /// Gets or sets the immutable numeric GitHub user id. Matching on the id (not the login) matters:
    /// logins can be renamed and a freed login can be registered by someone else.
    /// Find it with <c>curl -s https://api.github.com/users/&lt;login&gt; | jq .id</c>.
    /// </summary>
    public long GitHubId { get; set; }

    /// <summary>Gets or sets the login, for readability of the configuration only (not used for matching).</summary>
    public string? Login { get; set; }

    /// <summary>Gets or sets the role: <c>Admin</c> (can run maintenance actions) or <c>Member</c>.</summary>
    public string Role { get; set; } = PhotoRoles.Member;
}
