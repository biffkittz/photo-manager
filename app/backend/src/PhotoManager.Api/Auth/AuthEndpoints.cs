using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PhotoManager.Api.Audit;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Security;

namespace PhotoManager.Api.Auth;

/// <summary>Sign-in, sign-out and session endpoints.</summary>
public static class AuthEndpoints
{
    /// <summary>Maps <c>/api/auth/*</c>.</summary>
    /// <param name="api">The /api route group.</param>
    /// <returns>The same group.</returns>
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var auth = api.MapGroup("/auth").RequireRateLimiting(RateLimitPolicies.Auth);

        // Top-level navigation target (not fetch): starts the GitHub OAuth flow.
        auth.MapGet("/login", (string? returnUrl) =>
                Results.Challenge(new AuthenticationProperties { RedirectUri = ReturnUrl.Sanitize(returnUrl) }, [GitHubAuthenticationDefaults.AuthenticationScheme]))
            .AllowAnonymous();

        auth.MapGet("/session", (HttpContext http, IAntiforgery antiforgery) =>
            {
                if (http.User.Identity?.IsAuthenticated != true)
                {
                    return TypedResults.Ok(new SessionResponse(false, null, null));
                }

                var user = new UserInfo(
                    http.User.GitHubId(),
                    http.User.Login(),
                    http.User.FindFirst(PhotoClaims.DisplayName)?.Value,
                    http.User.FindFirst(PhotoClaims.AvatarUrl)?.Value,
                    http.User.IsInRole(PhotoRoles.Admin) ? PhotoRoles.Admin : PhotoRoles.Member);

                // The request token goes into the JSON body and the SPA keeps it in memory (never localStorage);
                // the matching cookie token is HttpOnly.
                var tokens = antiforgery.GetAndStoreTokens(http);
                return TypedResults.Ok(new SessionResponse(true, user, tokens.RequestToken));
            })
            .AllowAnonymous();

        auth.MapPost("/logout", async (HttpContext http, AuditLog audit) =>
        {
            await audit.WriteAsync("auth.signout", null, null);
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            http.Response.Headers[AuthenticationSetup.ClearSiteDataHeader] = AuthenticationSetup.ClearCacheDirective;
            return TypedResults.NoContent();
        });

        return api;
    }
}
