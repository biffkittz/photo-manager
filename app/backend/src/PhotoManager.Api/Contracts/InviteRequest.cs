namespace PhotoManager.Api.Contracts;

/// <summary>Invites someone.</summary>
/// <param name="Name">How the person appears in the app (1 to 100 characters).</param>
/// <param name="Email">Optional: the only Google address allowed to accept the invite.</param>
public sealed record InviteRequest(string? Name, string? Email);
