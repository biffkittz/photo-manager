using System.Security.Claims;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using PhotoManager.Api.Audit;

namespace PhotoManager.Api.Auth;

/// <summary>GitHub OAuth sign-in with an allowlist, cookie sessions and antiforgery.</summary>
public static class AuthenticationSetup
{
    /// <summary>Path GitHub redirects back to after sign-in; handled inside the authentication middleware.</summary>
    public const string CallbackPath = "/signin-github";

    /// <summary>Where a GitHub account that is not on the allowlist is sent.</summary>
    public const string DeniedRedirect = "/login?error=access_denied";

    /// <summary>Where any other failed GitHub sign-in is sent (the reason is logged, not shown).</summary>
    public const string FailedRedirect = "/login?error=signin_failed";

    internal const string ClearSiteDataHeader = "Clear-Site-Data";
    internal const string ClearCacheDirective = "\"cache\"";

    /// <summary>
    /// Registers authentication. Why the app runs its own OAuth flow instead of Container Apps' built-in auth:
    /// it behaves identically in local development and tests, and the allowlist plus session rules live in
    /// reviewed code rather than portal configuration.
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
            .ValidateOnStart();

        // In production every cookie is __Host- prefixed: Secure, Path=/, no Domain, so no sibling subdomain can
        // set or read it. Local development runs over plain http://localhost, where the prefix is not allowed.
        var secure = !environment.IsDevelopment();
        var cookiePrefix = secure ? "__Host-" : string.Empty;

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = $"{cookiePrefix}pm-session";
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

                // Re-check the allowlist on every request so removing a user takes effect immediately rather
                // than when their session cookie expires, and end sessions older than the absolute lifetime.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var auth = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<AuthOptions>>().CurrentValue;
                    var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                    var allowed = context.Principal is not null ? auth.Find(context.Principal.GitHubId()) : null;
                    var signedInAt = context.Principal?.SignedInAtUtc();
                    var expired = signedInAt is null || now - signedInAt.Value > auth.MaxSessionLifetime;
                    if (allowed is null || expired || !context.Principal!.IsInRole(allowed.Role))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        context.HttpContext.Response.Headers[ClearSiteDataHeader] = ClearCacheDirective;
                    }
                };
            })
            .AddGitHub(GitHubAuthenticationDefaults.AuthenticationScheme, options =>
            {
                var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
                options.ClientId = auth.GitHubClientId ?? string.Empty;
                options.ClientSecret = auth.GitHubClientSecret ?? string.Empty;
                options.CallbackPath = CallbackPath;
                options.UsePkce = true;
                options.SaveTokens = false;

                // Only the public profile is needed; no repository or email scopes are requested.
                options.Scope.Clear();
                options.ClaimActions.MapJsonKey(PhotoClaims.AvatarUrl, "avatar_url");
                options.ClaimActions.MapJsonKey(PhotoClaims.DisplayName, "name");
                options.CorrelationCookie.Name = $"{cookiePrefix}pm-correlation";
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

                // The allowlist is enforced here, after GitHub has identified the account and before any session
                // exists: HandleResponse() stops the sign-in, so a denied account never gets a cookie. (A Fail() in
                // OnCreatingTicket is not honoured by the OAuth handler.)
                options.Events.OnTicketReceived = async context =>
                {
                    var services = context.HttpContext.RequestServices;
                    var auth = services.GetRequiredService<IOptionsMonitor<AuthOptions>>().CurrentValue;
                    var audit = services.GetRequiredService<AuditLog>();
                    var principal = context.Principal!;
                    var gitHubId = principal.GitHubId();
                    var allowed = auth.Find(gitHubId);
                    if (allowed is null)
                    {
                        await audit.WriteAsync("auth.denied", gitHubId.ToString(System.Globalization.CultureInfo.InvariantCulture), new { login = principal.Login() }, actorOverride: principal.Login(), actorIdOverride: gitHubId);
                        context.Response.Redirect(DeniedRedirect);
                        context.HandleResponse();
                        return;
                    }

                    var signedInAt = services.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds();
                    var identity = (ClaimsIdentity)principal.Identity!;
                    identity.AddClaim(new Claim(ClaimTypes.Role, allowed.Role));
                    identity.AddClaim(new Claim(PhotoClaims.SignedInAt, signedInAt.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    await audit.WriteAsync("auth.signin", null, null, actorOverride: principal.Login(), actorIdOverride: gitHubId);
                };

                // Any other failure (the OAuth app's callback URL or credentials, a sign-in cancelled on GitHub, an
                // expired or replayed callback) is not "not allowed". The browser gets a generic page; the reason goes
                // to the log, where the operator can see it. It never contains a secret: it is the handler's message
                // and GitHub's error code.
                options.Events.OnRemoteFailure = context =>
                {
                    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(AuthenticationSetup))
                        .LogWarning("GitHub sign-in failed: {Reason}", context.Failure?.Message ?? "unknown");
                    context.Response.Redirect(FailedRedirect);
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

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
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });

        return services;
    }
}
