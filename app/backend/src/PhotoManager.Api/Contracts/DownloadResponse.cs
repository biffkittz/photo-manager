using PhotoManager.Core.Domain;

namespace PhotoManager.Api.Contracts;

/// <summary>State of a prepared download.</summary>
/// <param name="Id">Download id.</param>
/// <param name="Status">Pending, Ready or Failed.</param>
/// <param name="FileName">Filename offered to the browser.</param>
/// <param name="SizeBytes">Size once ready.</param>
/// <param name="Error">Failure message.</param>
/// <param name="ExpiresAt">When the content becomes unavailable.</param>
/// <param name="PhotoCount">Number of photos included.</param>
/// <param name="SkippedCount">Requested photos left out because they were no longer available.</param>
public sealed record DownloadResponse(Guid Id, DownloadStatus Status, string FileName, long? SizeBytes, string? Error, DateTimeOffset ExpiresAt, int PhotoCount, int SkippedCount);
