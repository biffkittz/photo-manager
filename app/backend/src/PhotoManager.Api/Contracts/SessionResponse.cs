namespace PhotoManager.Api.Contracts;

/// <summary>Current session.</summary>
/// <param name="Authenticated">Whether a user is signed in.</param>
/// <param name="User">The user, when signed in.</param>
/// <param name="CsrfToken">Antiforgery request token to send as <c>X-CSRF-TOKEN</c> on mutating calls (also before
/// signing in, for opening an invite).</param>
/// <param name="GoogleSignIn">Whether the sign-in page offers Google (for invited family members).</param>
public sealed record SessionResponse(bool Authenticated, UserInfo? User, string? CsrfToken, bool GoogleSignIn);
