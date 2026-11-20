using System.Buffers.Binary;

namespace PhotoManager.Core.Crypto;

/// <summary>Parsed form of the fixed 80-byte v2 header.</summary>
/// <param name="KeyType">How the master key is obtained.</param>
/// <param name="ChunkSize">Plaintext bytes per content chunk.</param>
/// <param name="KdfIterations">PBKDF2 iterations for <see cref="V2KeyType.Password"/>; zero otherwise.</param>
/// <param name="KeyId">8-byte identifier of the master key.</param>
/// <param name="KdfSalt">16-byte PBKDF2 salt (zeros for raw keys).</param>
/// <param name="FileSalt">32-byte random HKDF salt, unique per file.</param>
/// <param name="MetadataLength">Length of the encrypted metadata block including its tag; zero when absent.</param>
internal sealed record V2Header(
    V2KeyType KeyType,
    int ChunkSize,
    int KdfIterations,
    byte[] KeyId,
    byte[] KdfSalt,
    byte[] FileSalt,
    int MetadataLength)
{
    /// <summary>Serializes the header to its canonical 80-byte representation.</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[V2Format.HeaderSize];
        var span = bytes.AsSpan();
        V2Format.Magic.CopyTo(span);
        span[V2Format.VersionOffset] = V2Format.Version;
        span[V2Format.KeyTypeOffset] = (byte)KeyType;
        BinaryPrimitives.WriteUInt32BigEndian(span[V2Format.ChunkSizeOffset..], (uint)ChunkSize);
        BinaryPrimitives.WriteUInt32BigEndian(span[V2Format.KdfIterationsOffset..], (uint)KdfIterations);
        KeyId.CopyTo(span[V2Format.KeyIdOffset..]);
        KdfSalt.CopyTo(span[V2Format.KdfSaltOffset..]);
        FileSalt.CopyTo(span[V2Format.FileSaltOffset..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[V2Format.MetadataLengthOffset..], (uint)MetadataLength);
        return bytes;
    }

    /// <summary>
    /// Parses and strictly validates a header. Every field is bounds-checked before use because headers
    /// come from storage and may have been tampered with.
    /// </summary>
    /// <exception cref="PhotoDecryptionException">The header is not a valid v2 header.</exception>
    public static V2Header Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < V2Format.HeaderSize || !bytes[..V2Format.Magic.Length].SequenceEqual(V2Format.Magic))
        {
            throw new PhotoDecryptionException("Not a v2 encrypted file (bad magic).");
        }

        if (bytes[V2Format.VersionOffset] != V2Format.Version)
        {
            throw new PhotoDecryptionException($"Unsupported v2 format version {bytes[V2Format.VersionOffset]}.");
        }

        if (bytes[V2Format.ReservedOffset] != 0 || bytes[V2Format.ReservedOffset + 1] != 0)
        {
            throw new PhotoDecryptionException("Reserved header bytes must be zero.");
        }

        var keyType = (V2KeyType)bytes[V2Format.KeyTypeOffset];
        var chunkSize = BinaryPrimitives.ReadUInt32BigEndian(bytes[V2Format.ChunkSizeOffset..]);
        var iterations = BinaryPrimitives.ReadUInt32BigEndian(bytes[V2Format.KdfIterationsOffset..]);
        var metadataLength = BinaryPrimitives.ReadUInt32BigEndian(bytes[V2Format.MetadataLengthOffset..]);

        if (chunkSize is < V2Format.MinChunkSize or > V2Format.MaxChunkSize)
        {
            throw new PhotoDecryptionException("Invalid chunk size in header.");
        }

        switch (keyType)
        {
            case V2KeyType.Password when iterations is < V2Format.MinPasswordIterations or > V2Format.MaxPasswordIterations:
                throw new PhotoDecryptionException("Invalid KDF iteration count in header.");
            case V2KeyType.RawKey when iterations != 0:
                throw new PhotoDecryptionException("Raw-key headers must not specify KDF iterations.");
            case V2KeyType.Password:
            case V2KeyType.RawKey:
                break;
            default:
                throw new PhotoDecryptionException("Unknown key type in header.");
        }

        if (metadataLength != 0 && metadataLength is < V2Format.TagSize or > V2Format.MaxMetadataPlaintextSize + V2Format.TagSize)
        {
            throw new PhotoDecryptionException("Invalid metadata length in header.");
        }

        return new V2Header(
            keyType,
            (int)chunkSize,
            (int)iterations,
            bytes.Slice(V2Format.KeyIdOffset, V2Format.KeyIdSize).ToArray(),
            bytes.Slice(V2Format.KdfSaltOffset, V2Format.KdfSaltSize).ToArray(),
            bytes.Slice(V2Format.FileSaltOffset, V2Format.FileSaltSize).ToArray(),
            (int)metadataLength);
    }
}
