namespace PhotoManager.Core.Domain;

// Persisted as a string (see PhotoDbContext): renaming a member is a breaking change; adding is safe.
/// <summary>Lifecycle of a photo record.</summary>
public enum PhotoStatus
{
    /// <summary>Upload staged; ingest workflow queued or not yet started.</summary>
    Queued,

    /// <summary>Ingest workflow is running (inspect, describe, renditions, archive).</summary>
    Processing,

    /// <summary>Original is in the archive and the photo is browsable.</summary>
    Ready,

    /// <summary>Ingest failed permanently; <c>StatusDetail</c> explains why.</summary>
    Failed,

    /// <summary>The upload duplicated an existing photo (same SHA-256) and was not archived again.</summary>
    Duplicate,

    /// <summary>The archive object disappeared from the bucket (detected by re-indexing).</summary>
    Missing,
}
