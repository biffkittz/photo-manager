namespace PhotoManager.Core.Workflows;

/// <summary>
/// Workflow type names. The API starts workflows by name so it never references worker code;
/// each workflow class declares the same name in its <c>[Workflow]</c> attribute.
/// </summary>
public static class WorkflowTypes
{
    /// <summary>Ingests one uploaded photo.</summary>
    public const string PhotoIngest = "PhotoIngest";

    /// <summary>Decrypts originals into a web-key-encrypted download (single file or ZIP).</summary>
    public const string PrepareDownload = "PrepareDownload";

    /// <summary>Generates thumbnail/preview renditions for an archived photo.</summary>
    public const string EnsureRenditions = "EnsureRenditions";

    /// <summary>(Re)generates the AI description of an archived photo.</summary>
    public const string DescribePhoto = "DescribePhoto";

    /// <summary>Synchronizes the database with the archive bucket.</summary>
    public const string ReindexArchive = "ReindexArchive";

    /// <summary>Generates missing renditions or descriptions in bulk.</summary>
    public const string Backfill = "Backfill";
}
