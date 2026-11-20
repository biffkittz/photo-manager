namespace PhotoManager.Core.Crypto;

/// <summary>Finds the master key needed to open a v2 file, given the identity recorded in its header.</summary>
public interface IV2KeyResolver
{
    /// <summary>Returns the matching master key, or <see langword="null"/> when this process does not hold it.</summary>
    /// <param name="keyType">Key type from the header.</param>
    /// <param name="keyId">8-byte key identifier from the header.</param>
    /// <param name="kdfIterations">PBKDF2 iterations from the header (password keys only).</param>
    /// <param name="kdfSalt">PBKDF2 salt from the header (password keys only).</param>
    /// <returns>The key, or <see langword="null"/>.</returns>
    V2MasterKey? Resolve(V2KeyType keyType, ReadOnlySpan<byte> keyId, int kdfIterations, ReadOnlySpan<byte> kdfSalt);
}
