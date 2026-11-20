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

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method))
        {
            if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Missing or invalid CSRF token.");
            }
        }

        return await next(context);
    }
}
