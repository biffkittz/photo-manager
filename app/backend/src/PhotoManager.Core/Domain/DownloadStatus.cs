namespace PhotoManager.Core.Domain;

// Persisted as a string (see PhotoDbContext): renaming a member is a breaking change; adding is safe.
/// <summary>Lifecycle of a prepared download.</summary>
public enum DownloadStatus
{
    /// <summary>A worker is decrypting and packaging the photos.</summary>
    Pending,

    /// <summary>Ready to stream to the requester.</summary>
    Ready,

    /// <summary>Preparation failed.</summary>
    Failed,
}
