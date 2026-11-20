namespace PhotoManager.Infrastructure.Storage;

/// <summary>One page of a bucket listing.</summary>
/// <param name="Objects">Objects on this page.</param>
/// <param name="NextContinuationToken">Token for the next page, or <see langword="null"/> at the end.</param>
public sealed record ArchiveListPage(IReadOnlyList<ArchiveObjectInfo> Objects, string? NextContinuationToken);
