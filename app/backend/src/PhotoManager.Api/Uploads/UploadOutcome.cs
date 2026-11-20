using PhotoManager.Api.Contracts;

namespace PhotoManager.Api.Uploads;

/// <summary>Outcome of staging an upload.</summary>
/// <param name="Accepted">The staged photo, when accepted.</param>
/// <param name="Problem">The HTTP problem to return, when rejected.</param>
public sealed record UploadOutcome(UploadAccepted? Accepted, IResult? Problem);
