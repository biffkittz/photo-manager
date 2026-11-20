using System.Globalization;
using PhotoManager.Core.Domain;

namespace PhotoManager.Core.Workflows;

/// <summary>
/// Deterministic workflow IDs. Deriving IDs from domain identifiers makes workflow starts idempotent:
/// a retried HTTP request or a double click resolves to the same execution instead of duplicating work.
/// </summary>
public static class WorkflowIds
{
    /// <summary>ID of the Temporal Schedule that triggers the nightly re-index.</summary>
    public const string NightlyReindexSchedule = "nightly-reindex";

    /// <summary>Singleton ID of the archive re-index workflow.</summary>
    public const string ReindexArchive = "reindex-archive";

    /// <summary>Gets the ingest workflow ID for a photo.</summary>
    /// <param name="photoId">The photo identifier.</param>
    /// <returns>The workflow ID.</returns>
    public static string PhotoIngest(Guid photoId) => Format("photo-ingest", photoId);

    /// <summary>Gets the download preparation workflow ID.</summary>
    /// <param name="downloadId">The download identifier.</param>
    /// <returns>The workflow ID.</returns>
    public static string PrepareDownload(Guid downloadId) => Format("download", downloadId);

    /// <summary>Gets the rendition workflow ID for a photo.</summary>
    /// <param name="photoId">The photo identifier.</param>
    /// <returns>The workflow ID.</returns>
    public static string EnsureRenditions(Guid photoId) => Format("renditions", photoId);

    /// <summary>Gets the describe workflow ID for a photo.</summary>
    /// <param name="photoId">The photo identifier.</param>
    /// <returns>The workflow ID.</returns>
    public static string DescribePhoto(Guid photoId) => Format("describe", photoId);

    /// <summary>Gets the singleton backfill workflow ID for a job kind.</summary>
    /// <param name="kind">The backfill kind.</param>
    /// <returns>The workflow ID.</returns>
    public static string Backfill(JobKind kind) =>
        string.Create(CultureInfo.InvariantCulture, $"backfill-{kind.ToString().ToLowerInvariant()}");

    private static string Format(string prefix, Guid id) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}-{id:N}");
}
