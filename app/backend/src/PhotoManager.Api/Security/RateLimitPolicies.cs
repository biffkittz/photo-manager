using System.Threading.RateLimiting;
using PhotoManager.Api.Auth;

namespace PhotoManager.Api.Security;

/// <summary>Per-user (or per-IP when anonymous) rate limits.</summary>
public static class RateLimitPolicies
{
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
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "10";
                return ValueTask.CompletedTask;
            };

            options.AddPolicy(Api, http => Window(http, 1200, TimeSpan.FromMinutes(1)));
            options.AddPolicy(Auth, http => Window(http, 30, TimeSpan.FromMinutes(1)));
            options.AddPolicy(Upload, http => Window(http, 300, TimeSpan.FromMinutes(1)));
            options.AddPolicy(Expensive, http => Window(http, 30, TimeSpan.FromMinutes(1)));
        });

    private static RateLimitPartition<string> Window(HttpContext http, int permits, TimeSpan window)
    {
        // Partition by user when signed in; otherwise by client IP (the real client IP, because forwarded
        // headers from the Container Apps ingress are applied before the limiter runs).
        var key = http.User.Identity?.IsAuthenticated == true
            ? $"user:{http.User.GitHubId()}"
            : $"ip:{http.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0,
        });
    }
}
