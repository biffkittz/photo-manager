namespace PhotoManager.Api.Contracts;

/// <summary>One page of gallery results (keyset pagination).</summary>
/// <param name="Items">Photos on this page.</param>
/// <param name="NextCursor">Opaque cursor for the next page, or null at the end.</param>
public sealed record PhotoPage(IReadOnlyList<PhotoSummary> Items, string? NextCursor);
