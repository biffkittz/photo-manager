using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.Options;
using PhotoManager.Api.Audit;
using PhotoManager.Api.Members;

namespace PhotoManager.Api.Auth;

/// <summary>
/// Sign-in: GitHub for the accounts in configuration (the admins), Google for family members invited from the
/// People page; cookie sessions re-checked on every request; antiforgery.
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>Path GitHub redirects back to after sign-in; handled inside the authentication middleware.</summary>
    public const string CallbackPath = "/signin-github";

    /// <summary>Path Google redirects back to after sign-in; handled inside the authentication middleware.</summary>
    public const string GoogleCallbackPath = "/signin-google";

    /// <summary>Where a GitHub account that is not on the allowlist is sent.</summary>
    public const string DeniedRedirect = "/login?error=access_denied";

    /// <summary>Where a Google account that is not linked to a current member is sent.</summary>
    public const string GoogleDeniedRedirect = "/login?error=google_denied";

    /// <summary>Where any other failed sign-in is sent (the reason is logged, not shown).</summary>
    public const string FailedRedirect = "/login?error=signin_failed";

    internal const string ClearSiteDataHeader = "Clear-Site-Data";
    internal const string ClearCacheDirective = "\"cache\"";

    /// <summary>
    /// Registers authentication. Why the app runs its own OAuth flows instead of Container Apps' built-in auth:
    /// they behave identically in local development and tests, and the allowlist, the invite rules and the session
    /// rules live in reviewed code rather than portal configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">App configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPhotoAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<AuthOptions>()
            .BindConfiguration(AuthOptions.SectionName)
            .Validate(o => o.Users.Count > 0 && o.Users.All(u => u.GitHubId > 0), "Auth:Users must list at least one numeric GitHub id.")
            .Validate(o => o.Users.All(u => u.Role is PhotoRoles.Admin or PhotoRoles.Member), "Auth:Users roles must be Admin or Member.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.GitHubClientId) && !string.IsNullOrWhiteSpace(o.GitHubClientSecret), "Auth:GitHubClientId and Auth:GitHubClientSecret are required.")
            .Validate(o => string.IsNullOrWhiteSpace(o.GoogleClientId) == string.IsNullOrWhiteSpace(o.GoogleClientSecret), "Set both Auth:GoogleClientId and Auth:GoogleClientSecret (Google sign-in), or neither.")
            .ValidateOnStart();

        // Google is optional: the handler is registered only when it is configured (an unconfigured OAuth handler
        // would fail every request, because remote handlers are initialized to check for their callback path).
        var settings = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        // In production every cookie is __Host- prefixed: Secure, Path=/, no Domain, so no sibling subdomain can
        // set or read it. Local development runs over plain http://localhost, where the prefix is not allowed.
        // Browsers silently drop a __Host- cookie whose path is not "/", so every cookie sets it explicitly.
        var secure = !environment.IsDevelopment();
        var cookiePrefix = secure ? "__Host-" : string.Empty;

        var authentication = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = $"{cookiePrefix}pm-session";
                options.Cookie.Path = "/";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = true;

                // The SPA handles navigation; the API only ever answers with status codes.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };

                // Re-check access on every request so removing someone takes effect immediately rather than when
                // their session cookie expires, and end sessions older than the absolute lifetime.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var requestServices = context.HttpContext.RequestServices;
                    var auth = requestServices.GetRequiredService<IOptionsMonitor<AuthOptions>>().CurrentValue;
                    var now = requestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                    var signedInAt = context.Principal?.SignedInAtUtc();
                    var current = context.Principal is not null
                        && signedInAt is not null
                        && now - signedInAt.Value <= auth.MaxSessionLifetime
                        && await IsStillAllowedAsync(context.Principal, auth, requestServices, context.HttpContext.RequestAborted);
                    if (!current)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        context.HttpContext.Response.Headers[ClearSiteDataHeader] = ClearCacheDirective;
                    }
                };
            })
            .AddGitHub(GitHubAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.ClientId = settings.GitHubClientId ?? string.Empty;
                options.ClientSecret = settings.GitHubClientSecret ?? string.Empty;
                options.CallbackPath = CallbackPath;
                options.UsePkce = true;
                options.SaveTokens = false;

                // Only the public profile is needed; no repository or email scopes are requested.
                options.Scope.Clear();
                options.ClaimActions.MapJsonKey(PhotoClaims.AvatarUrl, "avatar_url");
                options.ClaimActions.MapJsonKey(PhotoClaims.DisplayName, "name");
                ConfigureCorrelationCookie(options, cookiePrefix, secure);

                // The allowlist is enforced here, after GitHub has identified the account and before any session
                // exists: HandleResponse() stops the sign-in, so a denied account never gets a cookie. (A Fail() in
                // OnCreatingTicket is not honoured by the OAuth handler.)
                options.Events.OnTicketReceived = async context =>
                {
                    var requestServices = context.HttpContext.RequestServices;
                    var auth = requestServices.GetRequiredService<IOptionsMonitor<AuthOptions>>().CurrentValue;
                    var audit = requestServices.GetRequiredService<AuditLog>();
                    var principal = context.Principal!;
                    var gitHubId = principal.UserId();
                    var allowed = auth.Find(gitHubId);
                    if (allowed is null)
                    {
                        await audit.WriteAsync("auth.denied", gitHubId.ToString(CultureInfo.InvariantCulture), new { login = principal.Login() }, actorOverride: principal.Login(), actorIdOverride: gitHubId);
                        context.Response.Redirect(DeniedRedirect);
                        context.HandleResponse();
                        return;
                    }

                    var identity = (ClaimsIdentity)principal.Identity!;
                    identity.AddClaim(new Claim(ClaimTypes.Role, allowed.Role));
                    identity.AddClaim(new Claim(PhotoClaims.Provider, SignInProviders.GitHub));
                    identity.AddClaim(new Claim(PhotoClaims.SignedInAt, SignedInNow(requestServices)));
                    await audit.WriteAsync("auth.signin", null, null, actorOverride: principal.Login(), actorIdOverride: gitHubId);
                };

                options.Events.OnRemoteFailure = context => OnRemoteFailure(context, "GitHub");
            });

        if (settings.GoogleSignInEnabled)
        {
            authentication.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
            {
                options.ClientId = settings.GoogleClientId!;
                options.ClientSecret = settings.GoogleClientSecret!;
                options.CallbackPath = GoogleCallbackPath;
                options.UsePkce = true;
                options.SaveTokens = false;

                // Only the account id and email address are needed (no name, photo or anything else).
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("email");
                options.ClaimActions.MapJsonKey(PhotoClaims.GoogleSubject, "sub");
                options.ClaimActions.MapCustomJson(PhotoClaims.GoogleEmailVerified, user =>
                    user.TryGetProperty("email_verified", out var verified) && verified.ValueKind == JsonValueKind.True ? "true" : null);
                ConfigureCorrelationCookie(options, cookiePrefix, secure);

                // Membership is decided here, as for GitHub: a Google account gets a session only if it is linked to
                // a current member, or is accepting a valid invite (which links it). The session holds only the
                // member id, name and role, never anything from Google except the account id it is bound to.
                options.Events.OnTicketReceived = async context =>
                {
                    var requestServices = context.HttpContext.RequestServices;
                    var principal = context.Principal!;
                    var subject = principal.FindFirstValue(PhotoClaims.GoogleSubject);
                    var invite = PendingInvite.ReadFrom(context.Properties);
                    if (invite is not null)
                    {
                        requestServices.GetRequiredService<InviteCookie>().Delete(context.Response);
                    }

                    if (string.IsNullOrEmpty(subject))
                    {
                        context.Response.Redirect(FailedRedirect);
                        context.HandleResponse();
                        return;
                    }

                    var account = new GoogleAccount(subject, principal.FindFirstValue(ClaimTypes.Email), principal.HasClaim(PhotoClaims.GoogleEmailVerified, "true"));
                    var result = await requestServices.GetRequiredService<MemberService>().SignInWithGoogleAsync(account, invite, context.HttpContext.RequestAborted);
                    if (result.Outcome != GoogleSignInOutcome.SignedIn)
                    {
                        context.Response.Redirect(result.Outcome switch
                        {
                            GoogleSignInOutcome.NotInvited => GoogleDeniedRedirect,
                            GoogleSignInOutcome.WrongAccount => "/login?error=invite_wrong_account",
                            GoogleSignInOutcome.AccountInUse => "/login?error=invite_account_in_use",
                            _ => "/login?error=invite_invalid",
                        });
                        context.HandleResponse();
                        return;
                    }

                    var member = result.Member!;
                    context.Principal = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, member.Id.ToString(CultureInfo.InvariantCulture)),
                        new Claim(ClaimTypes.Name, member.DisplayName),
                        new Claim(ClaimTypes.Role, PhotoRoles.Member),
                        new Claim(PhotoClaims.Provider, SignInProviders.Google),
                        new Claim(PhotoClaims.GoogleSubject, subject),
                        new Claim(PhotoClaims.SignedInAt, SignedInNow(requestServices)),
                    ], GoogleDefaults.AuthenticationScheme));
                };

                options.Events.OnRemoteFailure = context => OnRemoteFailure(context, "Google");
            });
        }

        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((cookie, auth) => cookie.ExpireTimeSpan = auth.Value.SessionLifetime);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireRole(PhotoRoles.Member, PhotoRoles.Admin)
                .Build())
            .AddPolicy(PhotoRoles.AdminPolicy, policy => policy.RequireRole(PhotoRoles.Admin));

        services.AddAntiforgery(options =>
        {
            // The token is only ever accepted from the header. Without this, a request lacking the header would make
            // antiforgery read the whole form body looking for a token field: for an upload, buffering up to the
            // request size limit of plaintext photo to a temporary file before the request is refused.
            options.HeaderName = AntiforgeryValidationFilter.HeaderName;
            options.SuppressReadingTokenFromFormBody = true;
            options.Cookie.Name = $"{cookiePrefix}pm-csrf";
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });

        services.AddSingleton<InviteCookie>();
        services.AddScoped<MemberService>();
        return services;
    }

    /// <summary>
    /// Whether a session's account may still use the app. GitHub sessions: the account is still in configuration
    /// with the same role. Google sessions: a member (never an admin) who has not been removed and is still linked
    /// to the Google account the session was signed in with.
    /// </summary>
    private static async Task<bool> IsStillAllowedAsync(ClaimsPrincipal principal, AuthOptions auth, IServiceProvider services, CancellationToken cancellationToken)
    {
        var userId = principal.UserId();
        switch (principal.SignInProvider())
        {
            case SignInProviders.GitHub:
                return auth.Find(userId) is { } allowed && principal.IsInRole(allowed.Role);
            case SignInProviders.Google:
                return userId < 0
                    && auth.GoogleSignInEnabled
                    && principal.IsInRole(PhotoRoles.Member)
                    && !principal.IsInRole(PhotoRoles.Admin)
                    && principal.FindFirstValue(PhotoClaims.GoogleSubject) is { Length: > 0 } subject
                    && await services.GetRequiredService<MemberService>().IsActiveAsync(userId, subject, cancellationToken);
            default:
                return false;
        }
    }

    // ASP.NET Core scopes this cookie to the callback path (/signin-github) by default, which the browser refuses
    // for a __Host- cookie: the cookie was never stored and every sign-in failed with "Correlation failed" in
    // production.
    private static void ConfigureCorrelationCookie(RemoteAuthenticationOptions options, string cookiePrefix, bool secure)
    {
        options.CorrelationCookie.Name = $"{cookiePrefix}pm-correlation";
        options.CorrelationCookie.Path = "/";
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    }

    // Any other failure (the OAuth client's callback URL or credentials, a sign-in cancelled at the provider, an
    // expired or replayed callback) is not "not allowed". The browser gets a generic page; the reason goes to the
    // log, where the operator can see it. It never contains a secret: it is the handler's message and the provider's
    // error code.
    private static Task OnRemoteFailure(RemoteFailureContext context, string provider)
    {
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AuthenticationSetup))
            .LogWarning("{Provider} sign-in failed: {Reason}", provider, context.Failure?.Message ?? "unknown");
        context.Response.Redirect(FailedRedirect);
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static string SignedInNow(IServiceProvider services) =>
        services.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}
