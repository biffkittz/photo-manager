namespace PhotoManager.Api.Contracts;

/// <summary>Opens an invite link.</summary>
/// <param name="Token">The token from the link.</param>
public sealed record RedeemInviteRequest(string? Token);
