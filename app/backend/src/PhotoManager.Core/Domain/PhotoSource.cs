namespace PhotoManager.Core.Domain;

// Persisted as a string (see PhotoDbContext): renaming a member is a breaking change; adding is safe.
/// <summary>Where a photo record came from.</summary>
public enum PhotoSource
{
    /// <summary>Discovered by indexing objects already in the bucket (for example from <c>protect.ps1</c>).</summary>
    Archive,

    /// <summary>Uploaded through the web app.</summary>
    Upload,
}
