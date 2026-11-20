namespace PhotoManager.Api.Contracts;

/// <summary>The signed-in user.</summary>
/// <param name="GitHubId">Numeric GitHub id.</param>
/// <param name="Login">GitHub login.</param>
/// <param name="Name">Display name.</param>
/// <param name="AvatarUrl">Avatar URL.</param>
/// <param name="Role">Admin or Member.</param>
public sealed record UserInfo(long GitHubId, string Login, string? Name, string? AvatarUrl, string Role);
