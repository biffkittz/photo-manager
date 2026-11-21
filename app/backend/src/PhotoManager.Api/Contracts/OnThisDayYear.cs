namespace PhotoManager.Api.Contracts;

/// <summary>Photos taken on today's date in one earlier year ("On this day").</summary>
/// <param name="Year">The year they were taken.</param>
/// <param name="YearsAgo">How many years ago that is.</param>
/// <param name="Photos">The photos, in the order they were taken.</param>
public sealed record OnThisDayYear(int Year, int YearsAgo, IReadOnlyList<PhotoSummary> Photos);
