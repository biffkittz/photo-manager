namespace PhotoManager.Worker.Activities;

/// <summary>Input of <see cref="DescribeActivities.DescribeArchivedPhotoAsync"/>.</summary>
/// <param name="PhotoId">The photo.</param>
/// <param name="KeepUserDescription">Leave a description written by a person untouched (backfills); false when a
/// person explicitly asked for a new AI description of this photo.</param>
public sealed record DescribeArchivedInput(Guid PhotoId, bool KeepUserDescription);
