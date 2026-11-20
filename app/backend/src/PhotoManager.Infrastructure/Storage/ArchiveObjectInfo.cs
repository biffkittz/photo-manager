namespace PhotoManager.Infrastructure.Storage;

/// <summary>Attributes of one archive object.</summary>
/// <param name="Key">Object key.</param>
/// <param name="Size">Stored (ciphertext) size in bytes.</param>
/// <param name="ETag">Entity tag without quotes.</param>
/// <param name="LastModified">Last modification time.</param>
public sealed record ArchiveObjectInfo(string Key, long Size, string ETag, DateTimeOffset LastModified);
