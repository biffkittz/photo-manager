namespace PhotoManager.Worker.Activities;

/// <summary>Input of the MarkDownloadFailed activity.</summary>
/// <param name="DownloadId">The download.</param>
/// <param name="Error">User-safe error.</param>
public sealed record DownloadFailedInput(Guid DownloadId, string Error);
