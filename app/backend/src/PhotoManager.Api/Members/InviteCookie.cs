using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace PhotoManager.Api.Members;

/// <summary>
/// Remembers, for 30 minutes, which invite this browser opened, between the invite page and the "Continue with
/// Google" sign-in. The invite token itself never leaves the page's address fragment and the request body; the
/// cookie holds only the member id and the token's hash, encrypted and authenticated with Data Protection.
/// </summary>
public sealed class InviteCookie
{
    /// <summary>How long an opened invite waits for the Google sign-in.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ITimeLimitedDataProtector _protector;
    private readonly bool _secure;

    /// <summary>Initializes a new instance of the <see cref="InviteCookie"/> class.</summary>
    /// <param name="dataProtection">Data Protection (the same key ring as the session cookie).</param>
    /// <param name="environment">Host environment: outside development the cookie is <c>__Host-</c> and Secure.</param>
    public InviteCookie(IDataProtectionProvider dataProtection, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(dataProtection);
        ArgumentNullException.ThrowIfNull(environment);
        _protector = dataProtection.CreateProtector("PhotoManager.Api.Members.InviteCookie.v1").ToTimeLimitedDataProtector();
        _secure = !environment.IsDevelopment();
        Name = $"{(_secure ? "__Host-" : string.Empty)}pm-invite";
    }

    /// <summary>Gets the cookie name.</summary>
    public string Name { get; }

    /// <summary>Stores an opened invite.</summary>
    /// <param name="response">The response.</param>
    /// <param name="invite">The invite.</param>
    internal void Append(HttpResponse response, PendingInvite invite)
    {
        var value = _protector.Protect($"{invite.MemberId.ToString(CultureInfo.InvariantCulture)}:{invite.TokenHash}", Lifetime);
        response.Cookies.Append(Name, value, Options(Lifetime));
    }

    /// <summary>Reads the opened invite, if the cookie is present, intact and not expired.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The invite, or null.</returns>
    internal PendingInvite? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(Name, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        string payload;
        try
        {
            payload = _protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            // Tampered, expired, or from a key ring that has since been rotated away: as if never opened.
            return null;
        }

        var separator = payload.IndexOf(':', StringComparison.Ordinal);
        return separator > 0
            && long.TryParse(payload.AsSpan(0, separator), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var memberId)
                ? new PendingInvite(memberId, payload[(separator + 1)..])
                : null;
    }

    /// <summary>Forgets the opened invite.</summary>
    /// <param name="response">The response.</param>
    internal void Delete(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    // SameSite=Lax: the cookie is read when the browser starts the Google sign-in (a same-site navigation).
    private CookieOptions Options(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = _secure,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = maxAge,
        IsEssential = true,
    };
}
