namespace PhotoManager.Infrastructure.Storage;

/// <summary>Logical areas of the private blob account. Everything stored in them is web-key encrypted (v2).</summary>
public enum BlobArea
{
    /// <summary>Uploads awaiting ingestion; lifecycle-deleted after one day.</summary>
    Staging,

    /// <summary>Thumbnails and previews.</summary>
    Renditions,

    /// <summary>Prepared downloads; lifecycle-deleted after one day.</summary>
    Downloads,
}
