using Microsoft.AspNetCore.Antiforgery;

namespace PhotoManager.Api.Auth;

/// <summary>
/// Requires a valid antiforgery token (header <c>X-CSRF-TOKEN</c>) on every state-changing API call.
/// The session cookie is SameSite=Lax, which already blocks most cross-site POSTs; the token is defence in
/// depth (Lax still allows top-level GET navigations, and older browsers behave differently).
/// </summary>
/// <param name="antiforgery">The antiforgery service.</param>
public sealed class AntiforgeryValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    /// <summary>Header carrying the request token.</summary>
    public const string HeaderName = "X-CSRF-TOKEN";

    /// <summary>
    /// Problem type of the rejection, so the SPA can tell a stale token (fetch a fresh one and retry once) from
    /// other 403 responses.
    /// </summary>
    public const string ProblemType = "urn:photo-manager:problem:csrf";

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var method = http.Request.Method;
        if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method))
        {
            // The header is required before IAntiforgery looks at the request at all, so a request without it is
            // refused without its body ever being read (see AuthenticationSetup: tokens are never taken from forms).
            if (string.IsNullOrEmpty(http.Request.Headers[HeaderName]) || !await IsValidAsync(http))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Missing or invalid CSRF token.", type: ProblemType);
            }
        }

        return await next(context);
    }

    private async Task<bool> IsValidAsync(HttpContext http)
    {
        try
        {
            return await antiforgery.IsRequestValidAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            // A token or cookie that cannot be read at all is just an invalid token, not a server error.
            return false;
        }
    }
}
