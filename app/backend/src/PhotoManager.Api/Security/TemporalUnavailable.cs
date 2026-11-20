using Temporalio.Exceptions;

namespace PhotoManager.Api.Security;

/// <summary>
/// Recognizes failures to reach Temporal when starting or cancelling work. Endpoints that record state before
/// starting a workflow use it to undo that state (so nothing is left "pending" forever) and answer 503.
/// </summary>
internal static class TemporalUnavailable
{
    /// <summary>True for RPC failures (other than "already started") and client-side timeouts.</summary>
    public static bool Matches(Exception exception) =>
        exception is (TemporalException and not WorkflowAlreadyStartedException) or TimeoutException or InvalidOperationException;

    /// <summary>The response returned to the browser.</summary>
    public static IResult Problem() =>
        TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Background processing is temporarily unavailable. Try again shortly.");
}
