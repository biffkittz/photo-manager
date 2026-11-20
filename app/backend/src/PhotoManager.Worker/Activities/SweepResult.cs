namespace PhotoManager.Worker.Activities;

/// <summary>Result of the SweepMissing activity.</summary>
/// <param name="MarkedMissing">Photos marked missing.</param>
/// <param name="Warning">Set when the sweep was skipped as implausible.</param>
public sealed record SweepResult(int MarkedMissing, string? Warning);
