namespace PhotoManager.Core.Domain;

// Persisted as a string (see PhotoDbContext): renaming a member is a breaking change; adding is safe.
/// <summary>Lifecycle of a maintenance job.</summary>
public enum JobStatus
{
    /// <summary>Created; workflow start requested.</summary>
    Queued,

    /// <summary>Workflow is running.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Completed,

    /// <summary>Finished with an error.</summary>
    Failed,

    /// <summary>Cancelled by a user.</summary>
    Cancelled,
}
