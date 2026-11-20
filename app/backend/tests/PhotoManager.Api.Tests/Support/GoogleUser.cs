namespace PhotoManager.Api.Tests.Support;

/// <summary>A Google account as the userinfo endpoint describes it.</summary>
public sealed record GoogleUser(string Subject, string? Email, bool EmailVerified);
