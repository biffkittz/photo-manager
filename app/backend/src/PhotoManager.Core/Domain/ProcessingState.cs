namespace PhotoManager.Core.Domain;

// Persisted as a string (see PhotoDbContext): renaming a member is a breaking change; adding is safe.
/// <summary>Progress of an optional derived artifact (renditions or AI description).</summary>
public enum ProcessingState
{
    /// <summary>Nothing has been attempted yet.</summary>
    NotStarted,

    /// <summary>Work is queued or running.</summary>
    Pending,

    /// <summary>The artifact exists.</summary>
    Ready,

    /// <summary>The last attempt failed; it can be retried.</summary>
    Failed,

    /// <summary>The source format cannot be processed (for example an unsupported image type).</summary>
    Unsupported,
}
