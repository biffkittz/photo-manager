namespace PhotoManager.Api.Contracts;

/// <summary>The signed-in user.</summary>
/// <param name="Id">User id: the numeric GitHub id (positive) or an invited member's id (negative).</param>
/// <param name="Login">GitHub login, or the member's name.</param>
/// <param name="Name">Display name.</param>
/// <param name="AvatarUrl">Avatar URL (GitHub accounts only).</param>
/// <param name="Role">Admin or Member.</param>
/// <param name="Provider"><c>github</c> or <c>google</c>.</param>
public sealed record UserInfo(long Id, string Login, string? Name, string? AvatarUrl, string Role, string Provider);
