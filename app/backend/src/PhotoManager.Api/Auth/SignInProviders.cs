namespace PhotoManager.Api.Auth;

/// <summary>The account providers people sign in with.</summary>
public static class SignInProviders
{
    /// <summary>GitHub: admins and members listed in configuration (Terraform <c>github_users</c>).</summary>
    public const string GitHub = "github";

    /// <summary>Google: people invited from the People page.</summary>
    public const string Google = "google";
}
