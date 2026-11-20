using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoManager.Api.Auth;

namespace PhotoManager.Api.Tests.Support;

/// <summary>
/// Test-only authentication: the GitHub id comes from the <c>X-Test-User</c> header. Roles come from the
/// real allowlist, so authorization behaves exactly as in production. Never registered outside tests.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<AuthOptions> auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var value) || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var gitHubId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, gitHubId.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, $"user{gitHubId}"),
        };
        if (auth.CurrentValue.Find(gitHubId) is { } allowed)
        {
            claims.Add(new Claim(ClaimTypes.Role, allowed.Role));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
