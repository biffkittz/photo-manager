namespace PhotoManager.Api.Contracts;

/// <summary>How many library photos were taken (else uploaded) in one month, in the viewer's time zone.</summary>
/// <param name="Year">Year.</param>
/// <param name="Month">Month (1 to 12).</param>
/// <param name="Count">Number of photos.</param>
public sealed record TimelineMonth(int Year, int Month, int Count);
