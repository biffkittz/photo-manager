using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PhotoManager.Core.Crypto;

/// <summary>
/// Constants and primitive helpers for the v2 format. See <c>docs/encryption.md</c> for the
/// normative specification; any change here must be reflected there and keep old files readable.
/// </summary>
internal static class V2Format
{
    public const byte Version = 2;
    public const int HeaderSize = 80;
    public const int TagSize = 16;
    public const int NonceSize = 12;
    public const int KeySize = 32;
    public const int KeyIdSize = 8;
    public const int KdfSaltSize = 16;
    public const int FileSaltSize = 32;
    public const int MinChunkSize = 1024;
    public const int MaxChunkSize = 16 * 1024 * 1024;
    public const int MaxMetadataPlaintextSize = 64 * 1024;

    // Bounds for password-derived keys. The upper bound stops a crafted header from forcing an
    // arbitrarily expensive PBKDF2 computation (CPU DoS) when we try to resolve its key.
    public const int MinPasswordIterations = 100_000;
    public const int MaxPasswordIterations = 2_000_000;

    // Header field offsets.
    public const int VersionOffset = 8;
    public const int KeyTypeOffset = 9;
    public const int ReservedOffset = 10;
    public const int ChunkSizeOffset = 12;
    public const int KdfIterationsOffset = 16;
    public const int KeyIdOffset = 20;
    public const int KdfSaltOffset = 28;
    public const int FileSaltOffset = 44;
    public const int MetadataLengthOffset = 76;

    /// <summary>
    /// Magic bytes, PNG-style: a high-bit byte (catches 7-bit transfers), "PMV2", then CR LF SUB
    /// (catches newline translation and stops <c>type</c>/<c>cat</c> on some platforms).
    /// </summary>
    public static ReadOnlySpan<byte> Magic => [0x89, 0x50, 0x4D, 0x56, 0x32, 0x0D, 0x0A, 0x1A];

    public static ReadOnlySpan<byte> KeyIdLabel => "photo-manager/v2/key-id"u8;

    public static ReadOnlySpan<byte> ContentKeyLabel => "photo-manager/v2/content"u8;

    public static ReadOnlySpan<byte> MetadataKeyLabel => "photo-manager/v2/metadata"u8;

    /// <summary>
    /// Builds the 96-bit GCM nonce for a content chunk: <c>0x000000 | counter (u64 BE) | final flag</c>.
    /// </summary>
    /// <remarks>
    /// Every file gets a unique content key (HKDF over a random 256-bit salt), so a counter-based nonce
    /// can never repeat under the same key. Encoding the chunk index prevents reordering/dropping
    /// chunks; encoding the final flag prevents truncation at a chunk boundary and appending data
    /// (the STREAM construction of Hoang et al.).
    /// </remarks>
    public static void ComputeChunkNonce(long counter, bool isFinal, Span<byte> nonce)
    {
        nonce.Clear();
        BinaryPrimitives.WriteInt64BigEndian(nonce.Slice(3, 8), counter);
        nonce[NonceSize - 1] = isFinal ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// Derives the per-file content and metadata keys. The complete 80-byte header is part of the HKDF
    /// <c>info</c>, so modifying any header field (chunk size, key id, salts, metadata length) yields
    /// different keys and every authentication check fails.
    /// </summary>
    public static (byte[] ContentKey, byte[] MetadataKey) DeriveFileKeys(
        ReadOnlySpan<byte> masterKey,
        ReadOnlySpan<byte> header)
    {
        Span<byte> prk = stackalloc byte[KeySize];
        HKDF.Extract(HashAlgorithmName.SHA256, masterKey, header.Slice(FileSaltOffset, FileSaltSize), prk);
        try
        {
            return (Expand(prk, ContentKeyLabel, header), Expand(prk, MetadataKeyLabel, header));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
        }
    }

    /// <summary>Computes the 8-byte key identifier stored in headers (HMAC, so it reveals nothing about the key).</summary>
    public static byte[] ComputeKeyId(ReadOnlySpan<byte> masterKey) =>
        HMACSHA256.HashData(masterKey, KeyIdLabel)[..KeyIdSize];

    private static byte[] Expand(ReadOnlySpan<byte> prk, ReadOnlySpan<byte> label, ReadOnlySpan<byte> header)
    {
        Span<byte> info = stackalloc byte[label.Length + HeaderSize];
        label.CopyTo(info);
        header[..HeaderSize].CopyTo(info[label.Length..]);
        var key = new byte[KeySize];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, key, info);
        return key;
    }
}
