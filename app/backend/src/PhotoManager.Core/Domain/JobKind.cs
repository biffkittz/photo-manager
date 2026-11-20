namespace PhotoManager.Core.Domain;

// Persisted as a string (see PhotoDbContext): renaming a member is a breaking change; adding is safe.
/// <summary>Kinds of long-running maintenance jobs started from the Actions page.</summary>
public enum JobKind
{
    /// <summary>Re-index the archive bucket into the database.</summary>
    ReindexArchive,

    /// <summary>Generate missing thumbnails/previews for archived photos.</summary>
    BackfillRenditions,

    /// <summary>Generate missing AI descriptions for archived photos.</summary>
    BackfillDescriptions,
}
