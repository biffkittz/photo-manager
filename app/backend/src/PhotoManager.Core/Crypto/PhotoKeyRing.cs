using System.Collections.Concurrent;

namespace PhotoManager.Core.Crypto;

/// <summary>
/// The set of keys held by one process. This is where the split-key design is enforced in code:
/// the web tier builds a ring with no archive password, so it cannot decrypt archive originals or
/// write archive files even through a bug. The secret is simply absent from its configuration.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Archive password</b> (workers only): decrypts legacy <c>protect.ps1</c> files and
/// derives the v2 archive master key (PBKDF2-HMAC-SHA256, cached per salt/iteration pair).</item>
/// <item><b>Web keys</b> (web + workers): random 256-bit keys for renditions, staged uploads and prepared
/// downloads. The first key encrypts; all keys decrypt, which allows zero-downtime rotation.</item>
/// </list>
/// </remarks>
public sealed class PhotoKeyRing : IV2KeyResolver, IDisposable
{
    private readonly string? _archivePassword;
    private readonly byte[] _archiveKdfSalt;
    private readonly int _archiveKdfIterations;
    private readonly V2MasterKey[] _webKeys;

    // Password-derived master keys by "iterations:salt". Only keys that are proven genuine enter the cache: the
    // configured archive key, and keys whose id matches the header of a file being read (only the archive
    // password can produce a matching id). A crafted header naming endless salts therefore cannot grow it.
    private readonly ConcurrentDictionary<string, Lazy<V2MasterKey>> _passwordKeys = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="PhotoKeyRing"/> class.</summary>
    /// <param name="archivePassword">The archive password, or <see langword="null"/> in the web tier.</param>
    /// <param name="webKeys">Raw 32-byte web keys; the first one is used for encryption.</param>
    /// <param name="archiveKdfSalt">16-byte PBKDF2 salt used when <em>writing</em> v2 archive files.</param>
    /// <param name="archiveKdfIterations">PBKDF2 iterations used when writing v2 archive files.</param>
    public PhotoKeyRing(
        string? archivePassword,
        IEnumerable<byte[]> webKeys,
        ReadOnlySpan<byte> archiveKdfSalt,
        int archiveKdfIterations)
    {
        ArgumentNullException.ThrowIfNull(webKeys);
        if (archiveKdfSalt.Length != V2Format.KdfSaltSize)
        {
            throw new ArgumentException($"Archive KDF salt must be {V2Format.KdfSaltSize} bytes.", nameof(archiveKdfSalt));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(archiveKdfIterations, V2Format.MinPasswordIterations);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(archiveKdfIterations, V2Format.MaxPasswordIterations);

        _archivePassword = string.IsNullOrEmpty(archivePassword) ? null : archivePassword;
        _archiveKdfSalt = archiveKdfSalt.ToArray();
        _archiveKdfIterations = archiveKdfIterations;
        _webKeys = [.. webKeys.Select(k => V2MasterKey.FromRawKey(k))];
    }

    /// <summary>Gets a value indicating whether this process holds the archive password.</summary>
    public bool CanAccessArchive => _archivePassword is not null;

    /// <summary>Gets a value indicating whether this process holds at least one web key.</summary>
    public bool HasWebKey => _webKeys.Length > 0;

    /// <summary>Gets the key used to encrypt new renditions, staged uploads and downloads.</summary>
    /// <exception cref="PhotoKeyUnavailableException">No web key is configured.</exception>
    public V2MasterKey ActiveWebKey => _webKeys.Length > 0
        ? _webKeys[0]
        : throw new PhotoKeyUnavailableException("No web key is configured (Crypto:WebKey).");

    /// <summary>Gets the key used to encrypt new archive originals (derived on first use, then cached).</summary>
    /// <exception cref="PhotoKeyUnavailableException">The archive password is not available in this process.</exception>
    public V2MasterKey ActiveArchiveKey => GetConfiguredArchiveKey()
        ?? throw new PhotoKeyUnavailableException("The archive key is not available in this process.");

    /// <summary>Returns the archive password for reading legacy <c>protect.ps1</c> files.</summary>
    /// <returns>The password.</returns>
    /// <exception cref="PhotoKeyUnavailableException">The archive password is not available in this process.</exception>
    public string GetLegacyArchivePassword() =>
        _archivePassword ?? throw new PhotoKeyUnavailableException("The archive key is not available in this process.");

    /// <inheritdoc />
    public V2MasterKey? Resolve(V2KeyType keyType, ReadOnlySpan<byte> keyId, int kdfIterations, ReadOnlySpan<byte> kdfSalt)
    {
        switch (keyType)
        {
            case V2KeyType.RawKey:
                foreach (var key in _webKeys)
                {
                    if (key.Matches(keyType, keyId))
                    {
                        return key;
                    }
                }

                return null;

            case V2KeyType.Password:
                return ResolvePasswordKey(keyId, kdfIterations, kdfSalt);

            default:
                return null;
        }
    }

    /// <summary>Gets the number of cached password-derived keys (tests verify that bogus headers cannot grow it).</summary>
    internal int CachedPasswordKeyCount => _passwordKeys.Count;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var key in _webKeys)
        {
            key.Dispose();
        }

        foreach (var lazy in _passwordKeys.Values.Where(l => l.IsValueCreated))
        {
            lazy.Value.Dispose();
        }
    }

    private static string CacheKey(ReadOnlySpan<byte> kdfSalt, int iterations) =>
        $"{iterations}:{Convert.ToHexString(kdfSalt)}";

    private V2MasterKey? GetConfiguredArchiveKey()
    {
        if (_archivePassword is null)
        {
            return null;
        }

        var password = _archivePassword;
        var salt = _archiveKdfSalt;
        var iterations = _archiveKdfIterations;
        return _passwordKeys.GetOrAdd(
            CacheKey(salt, iterations),
            _ => new Lazy<V2MasterKey>(() => V2MasterKey.FromPassword(password, salt, iterations), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private V2MasterKey? ResolvePasswordKey(ReadOnlySpan<byte> keyId, int iterations, ReadOnlySpan<byte> kdfSalt)
    {
        if (_archivePassword is null)
        {
            return null;
        }

        var cacheKey = CacheKey(kdfSalt, iterations);
        if (_passwordKeys.TryGetValue(cacheKey, out var cached))
        {
            var key = cached.Value;
            return key.Matches(V2KeyType.Password, keyId) ? key : null;
        }

        // Derive outside the cache; keep the key only if it is the one the file names, otherwise wipe it.
        var derived = V2MasterKey.FromPassword(_archivePassword, kdfSalt.ToArray(), iterations);
        if (!derived.Matches(V2KeyType.Password, keyId))
        {
            derived.Dispose();
            return null;
        }

        var winner = _passwordKeys.GetOrAdd(cacheKey, new Lazy<V2MasterKey>(() => derived)).Value;
        if (!ReferenceEquals(winner, derived))
        {
            // Another thread cached the same key first.
            derived.Dispose();
        }

        return winner;
    }
}
