namespace PhotoManager.Core.Workflows;

/// <summary>Counters accumulated by a re-index run.</summary>
/// <param name="Pages">Bucket pages processed.</param>
/// <param name="ObjectsSeen">Photo objects seen.</param>
/// <param name="Added">New photo records created.</param>
/// <param name="Updated">Existing records whose archive attributes changed.</param>
/// <param name="MarkedMissing">Records whose object no longer exists.</param>
public sealed record ReindexProgress(int Pages = 0, int ObjectsSeen = 0, int Added = 0, int Updated = 0, int MarkedMissing = 0)
{
    /// <summary>Adds the counters of one page.</summary>
    /// <param name="page">The page result.</param>
    /// <returns>The combined counters.</returns>
    public ReindexProgress Add(ReindexProgress page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new(Pages + page.Pages, ObjectsSeen + page.ObjectsSeen, Added + page.Added, Updated + page.Updated, MarkedMissing + page.MarkedMissing);
    }
}
