namespace PhotoManager.Api.Contracts;

/// <summary>Current session state for the SPA.</summary>
/// <param name="Authenticated">Whether a user is signed in.</param>
/// <param name="User">The user, when signed in.</param>
/// <param name="CsrfToken">Antiforgery request token to send as <c>X-CSRF-TOKEN</c> on mutating calls.</param>
public sealed record SessionResponse(bool Authenticated, UserInfo? User, string? CsrfToken);
