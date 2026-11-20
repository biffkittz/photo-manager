using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PhotoManager.Api.Auth;

namespace PhotoManager.Api.Security;

/// <summary>Per-user (or per-IP when anonymous) rate limits.</summary>
public static class RateLimitPolicies
{
    private const int AuthPermitsPerMinute = 30;

    /// <summary>General API traffic (the gallery requests many thumbnails).</summary>
    public const string Api = "api";

    /// <summary>Sign-in endpoints (anonymous; per IP).</summary>
    public const string Auth = "auth";

    /// <summary>Uploads.</summary>
    public const string Upload = "upload";

    /// <summary>Download preparation and expensive single-photo actions.</summary>
    public const string Expensive = "expensive";

    /// <summary>Registers the limiter policies.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPhotoRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = OnRejected;

            options.AddPolicy(Api, http => Window(http, 1200, TimeSpan.FromMinutes(1)));
            options.AddPolicy(Auth, http => Window(http, AuthPermitsPerMinute, TimeSpan.FromMinutes(1)));
            options.AddPolicy(Upload, http => Window(http, 300, TimeSpan.FromMinutes(1)));
            options.AddPolicy(Expensive, http => Window(http, 30, TimeSpan.FromMinutes(1)));
        });

    /// <summary>
    /// Options for the limiter in front of the OAuth callbacks (GitHub and Google). The callback is handled by the authentication
    /// middleware, so endpoint policies never see it; this limiter runs before authentication and therefore always
    /// partitions by client IP. Each callback costs the server requests to the provider (and, for accounts that are not
    /// allowed, an audit row), so it gets the same budget as the other sign-in endpoints.
    /// </summary>
    /// <returns>Options with a global per-IP limiter.</returns>
    public static RateLimiterOptions OAuthCallbackOptions() => new()
    {
        RejectionStatusCode = StatusCodes.Status429TooManyRequests,
        OnRejected = OnRejected,
        GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => Window(http, AuthPermitsPerMinute, TimeSpan.FromMinutes(1))),
    };

    private static ValueTask OnRejected(OnRejectedContext context, CancellationToken cancellationToken)
    {
        context.HttpContext.Response.Headers.RetryAfter = "10";
        return ValueTask.CompletedTask;
    }

    private static RateLimitPartition<string> Window(HttpContext http, int permits, TimeSpan window)
    {
        // Partition by user when signed in; otherwise by client IP (the real client IP, because forwarded
        // headers from the Container Apps ingress are applied before the limiter runs).
        var key = http.User.Identity?.IsAuthenticated == true
            ? $"user:{http.User.UserId()}"
            : $"ip:{http.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0,
        });
    }
}
