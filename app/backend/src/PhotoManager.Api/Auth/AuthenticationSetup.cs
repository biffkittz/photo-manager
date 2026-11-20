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
                    }
                };
            })
            .AddGitHub(GitHubAuthenticationDefaults.AuthenticationScheme, options =>
            {
                var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
                options.ClientId = auth.GitHubClientId ?? string.Empty;
                options.ClientSecret = auth.GitHubClientSecret ?? string.Empty;
                options.CallbackPath = "/signin-github";
                options.UsePkce = true;
                options.SaveTokens = false;

                // Only the public profile is needed; no repository or email scopes are requested.
                options.Scope.Clear();
                options.ClaimActions.MapJsonKey(PhotoClaims.AvatarUrl, "avatar_url");
                options.ClaimActions.MapJsonKey(PhotoClaims.DisplayName, "name");
                options.CorrelationCookie.Name = $"{cookiePrefix}pm-correlation";
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

                options.Events.OnCreatingTicket = async context =>
                {
                    var auth = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<AuthOptions>>().CurrentValue;
                    var audit = context.HttpContext.RequestServices.GetRequiredService<AuditLog>();
                    var principal = new ClaimsPrincipal(context.Identity!);
                    var allowed = auth.Find(principal.GitHubId());
                    if (allowed is null)
                    {
                        await audit.WriteAsync("auth.denied", principal.GitHubId().ToString(System.Globalization.CultureInfo.InvariantCulture), new { login = principal.Login() }, actorOverride: principal.Login(), actorIdOverride: principal.GitHubId());
                        context.Fail("This GitHub account is not allowed to use Photo Manager.");
                        return;
                    }

                    var signedInAt = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds();
                    context.Identity!.AddClaim(new Claim(ClaimTypes.Role, allowed.Role));
                    context.Identity.AddClaim(new Claim(PhotoClaims.SignedInAt, signedInAt.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    await audit.WriteAsync("auth.signin", null, null, actorOverride: principal.Login(), actorIdOverride: principal.GitHubId());
                };

                // Never show handler error details to the browser; the SPA renders a friendly page.
                options.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect("/login?error=access_denied");
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
            options.HeaderName = AntiforgeryValidationFilter.HeaderName;
            options.Cookie.Name = $"{cookiePrefix}pm-csrf";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });

        return services;
    }
}
