using PhotoManager.Core.Workflows;

namespace PhotoManager.Worker.Activities;

/// <summary>Result of indexing one page of the bucket.</summary>
/// <param name="NextContinuationToken">Next page token, or null at the end.</param>
/// <param name="Page">Counters for this page.</param>
public sealed record IndexPageResult(string? NextContinuationToken, ReindexProgress Page);
