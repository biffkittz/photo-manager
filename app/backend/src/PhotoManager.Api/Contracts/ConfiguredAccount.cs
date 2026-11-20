namespace PhotoManager.Api.Contracts;

/// <summary>A GitHub account allowed by configuration (Terraform <c>github_users</c>); not editable in the app.</summary>
/// <param name="Login">GitHub login (or the numeric id when the configuration has no login).</param>
/// <param name="Role"><c>Admin</c> or <c>Member</c>.</param>
public sealed record ConfiguredAccount(string Login, string Role);
