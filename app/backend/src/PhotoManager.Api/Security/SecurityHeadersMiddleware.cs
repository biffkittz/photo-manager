namespace PhotoManager.Api.Security;

/// <summary>
/// Adds browser security headers to every response.
/// </summary>
/// <remarks>
/// The CSP allows only same-origin scripts and styles: the Vite build emits external JS/CSS files and the
/// SPA never uses inline scripts, <c>eval</c> or <c>v-html</c>. Images may also be <c>blob:</c> (lightbox)
/// and GitHub avatars. Framing is forbidden (clickjacking), and nothing may be embedded cross-origin.
/// </remarks>
/// <param name="next">Next middleware.</param>
/// <param name="environment">Host environment.</param>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private readonly string _contentSecurityPolicy = string.Join(
        "; ",
        "default-src 'self'",
        "script-src 'self'",
        "style-src 'self'",
        "img-src 'self' blob: data: https://avatars.githubusercontent.com",
        "font-src 'self'",
        "connect-src 'self'",
        "media-src 'self'",
        "object-src 'none'",
        "frame-ancestors 'none'",
        "base-uri 'self'",
        "form-action 'self'",
        environment.IsDevelopment() ? string.Empty : "upgrade-insecure-requests").TrimEnd(' ', ';');

    /// <summary>Applies the headers and invokes the next middleware.</summary>
    /// <param name="context">HTTP context.</param>
    /// <returns>A task.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = _contentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";

            // Personal data must not be stored by shared caches; endpoints that serve immutable,
            // per-user images set their own private caching.
            if (context.Request.Path.StartsWithSegments("/api") && !headers.ContainsKey("Cache-Control"))
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
