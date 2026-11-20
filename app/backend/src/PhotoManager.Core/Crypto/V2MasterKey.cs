using System.Security.Cryptography;

namespace PhotoManager.Core.Crypto;

/// <summary>
/// A 256-bit v2 master key plus the header parameters that identify it. Instances are immutable and
/// safe to share across threads; <see cref="Dispose"/> wipes the key bytes.
/// </summary>
public sealed class V2MasterKey : IDisposable
{
    private readonly byte[] _key;
    private readonly byte[] _kdfSalt;
    private readonly byte[] _keyId;
    private volatile bool _disposed;

    private V2MasterKey(V2KeyType keyType, byte[] key, int kdfIterations, byte[] kdfSalt)
    {
        KeyType = keyType;
        _key = key;
        KdfIterations = kdfIterations;
        _kdfSalt = kdfSalt;
        _keyId = V2Format.ComputeKeyId(key);
    }

    /// <summary>Gets how this key was obtained.</summary>
    public V2KeyType KeyType { get; }

    /// <summary>Gets the PBKDF2 iteration count (zero for raw keys).</summary>
    public int KdfIterations { get; }

    /// <summary>Gets the PBKDF2 salt (all zeros for raw keys).</summary>
    public ReadOnlySpan<byte> KdfSalt => _kdfSalt;

    /// <summary>Gets the 8-byte key identifier written into file headers.</summary>
    public ReadOnlySpan<byte> KeyId => _keyId;

    /// <summary>Gets the key identifier as lowercase hex, for logs and diagnostics.</summary>
    public string KeyIdHex => Convert.ToHexStringLower(_keyId);

    /// <summary>Gets the raw key bytes. Internal so key material never leaves the crypto layer.</summary>
    internal ReadOnlySpan<byte> KeyBytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _key;
        }
    }

    /// <summary>Wraps a random 256-bit key (the "web key").</summary>
    /// <param name="key">Exactly 32 bytes.</param>
    /// <returns>The master key.</returns>
    /// <exception cref="ArgumentException">The key is not 32 bytes.</exception>
    public static V2MasterKey FromRawKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != V2Format.KeySize)
        {
            throw new ArgumentException($"Raw keys must be exactly {V2Format.KeySize} bytes.", nameof(key));
        }

        return new V2MasterKey(V2KeyType.RawKey, key.ToArray(), 0, new byte[V2Format.KdfSaltSize]);
    }

    /// <summary>
    /// Derives a master key from a password with PBKDF2-HMAC-SHA256. This is deliberately slow
    /// (hundreds of milliseconds at the default iteration count); callers cache the result.
    /// </summary>
    /// <param name="password">The archive password.</param>
    /// <param name="kdfSalt">16-byte salt recorded in every header written with this key.</param>
    /// <param name="iterations">PBKDF2 iteration count.</param>
    /// <returns>The derived master key.</returns>
    public static V2MasterKey FromPassword(string password, ReadOnlySpan<byte> kdfSalt, int iterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (kdfSalt.Length != V2Format.KdfSaltSize)
        {
            throw new ArgumentException($"KDF salt must be exactly {V2Format.KdfSaltSize} bytes.", nameof(kdfSalt));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, V2Format.MinPasswordIterations);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(iterations, V2Format.MaxPasswordIterations);

        var key = Rfc2898DeriveBytes.Pbkdf2(password, kdfSalt, iterations, HashAlgorithmName.SHA256, V2Format.KeySize);
        return new V2MasterKey(V2KeyType.Password, key, iterations, kdfSalt.ToArray());
    }

    /// <summary>Returns true when this key matches the identity recorded in a header.</summary>
    internal bool Matches(V2KeyType keyType, ReadOnlySpan<byte> keyId) =>
        KeyType == keyType && _keyId.AsSpan().SequenceEqual(keyId);

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        CryptographicOperations.ZeroMemory(_key);
    }
}
