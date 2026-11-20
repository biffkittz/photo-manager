using System.Security.Cryptography;

namespace PhotoManager.Worker.Activities;

/// <summary>
/// A decrypted image held in memory for the duration of one activity. Plaintext never touches disk;
/// the buffer is zeroed on dispose so it does not linger in memory (or a crash dump) after use.
/// </summary>
/// <param name="content">Seekable plaintext.</param>
/// <param name="sha256">Lowercase hex SHA-256 of the plaintext.</param>
public sealed class LoadedImage(MemoryStream content, string sha256) : IDisposable
{
    /// <summary>Gets the seekable plaintext.</summary>
    public MemoryStream Content { get; } = content;

    /// <summary>Gets the lowercase hex SHA-256 of the plaintext.</summary>
    public string Sha256 { get; } = sha256;

    /// <summary>Gets the plaintext length.</summary>
    public long Length => Content.Length;

    /// <inheritdoc />
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(Content.GetBuffer());
        Content.Dispose();
    }
}
