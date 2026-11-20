namespace PhotoManager.Worker.Activities;

/// <summary>Input of <see cref="MaintenanceActivities.SweepMissingAsync"/>.</summary>
/// <param name="SeenBefore">Photos not seen since this instant are candidates.</param>
/// <param name="ObjectsSeen">How many archive objects the completed scan saw.</param>
/// <param name="Force">Apply the sweep even when it exceeds the safety limit (an admin confirmed it).</param>
public sealed record SweepInput(DateTimeOffset SeenBefore, int ObjectsSeen, bool Force = false);
