using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.Options;
using PhotoManager.Api.Audit;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Members;
using PhotoManager.Api.Security;

namespace PhotoManager.Api.Auth;

/// <summary>Sign-in, sign-out, invite and session endpoints.</summary>
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

        // Top-level navigation target: starts the Google OAuth flow. After the invite page, it carries the opened
        // invite in the (encrypted) OAuth state and lets the person choose which Google account to link.
        auth.MapGet("/login/google", (string? returnUrl, HttpContext http, IOptionsMonitor<AuthOptions> options, InviteCookie inviteCookie) =>
            {
                if (!options.CurrentValue.GoogleSignInEnabled)
                {
                    return Results.NotFound();
                }

                var properties = new GoogleChallengeProperties { RedirectUri = ReturnUrl.Sanitize(returnUrl) };
                if (inviteCookie.Read(http.Request) is { } invite)
                {
                    invite.WriteTo(properties);
                    properties.Prompt = "select_account";
                }

                return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
            })
            .AllowAnonymous();

        // Called by the invite page with the token from the link's #fragment (so it is never in a URL the server,
        // a proxy or a log sees). Remembers the invite for the Google sign-in that follows.
        auth.MapPost("/invites/redeem", async (RedeemInviteRequest request, HttpContext http, MemberService members, InviteCookie inviteCookie, IOptionsMonitor<AuthOptions> options, CancellationToken ct) =>
            {
                var member = options.CurrentValue.GoogleSignInEnabled ? await members.FindPendingInviteAsync(request.Token, ct) : null;
                if (member is null)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "This invite link does not work. It may have expired, been used already, or been replaced by a newer link.");
                }

                inviteCookie.Append(http.Response, new PendingInvite(member.Id, member.InviteTokenHash!));
                return Results.Ok(new InvitePreview(member.DisplayName, member.InvitedBy, member.InviteExpiresAt!.Value, member.InviteEmail is not null));
            })
            .AllowAnonymous();

        auth.MapGet("/session", (HttpContext http, IAntiforgery antiforgery, IOptionsMonitor<AuthOptions> options) =>
            {
                // The request token goes into the JSON body and the SPA keeps it in memory (never localStorage);
                // the matching cookie token is HttpOnly. Signed-out visitors get one too, for opening an invite.
                var tokens = antiforgery.GetAndStoreTokens(http);
                var google = options.CurrentValue.GoogleSignInEnabled;
                if (http.User.Identity?.IsAuthenticated != true)
                {
                    return TypedResults.Ok(new SessionResponse(false, null, tokens.RequestToken, google));
                }

                var user = new UserInfo(
                    http.User.UserId(),
                    http.User.Login(),
                    http.User.FindFirst(PhotoClaims.DisplayName)?.Value,
                    http.User.FindFirst(PhotoClaims.AvatarUrl)?.Value,
                    http.User.IsInRole(PhotoRoles.Admin) ? PhotoRoles.Admin : PhotoRoles.Member,
                    http.User.SignInProvider());
                return TypedResults.Ok(new SessionResponse(true, user, tokens.RequestToken, google));
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
