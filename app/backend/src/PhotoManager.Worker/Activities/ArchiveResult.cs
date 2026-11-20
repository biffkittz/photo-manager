namespace PhotoManager.Worker.Activities;

/// <summary>Where an original was stored.</summary>
/// <param name="Key">Archive object key.</param>
/// <param name="Size">Ciphertext size.</param>
/// <param name="ETag">Object ETag.</param>
public sealed record ArchiveResult(string Key, long Size, string ETag);
