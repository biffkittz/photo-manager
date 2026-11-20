using System.Security.Cryptography;
using Google.Protobuf;
using Temporalio.Api.Common.V1;
using Temporalio.Converters;

namespace PhotoManager.Infrastructure.Temporal;

/// <summary>
/// Temporal payload codec that AES-256-GCM encrypts every payload (workflow/activity inputs and results,
/// heartbeat details, memos) before it leaves the process. Temporal Cloud stores only ciphertext, so
/// filenames, descriptions and object keys are never visible there (Temporal security best practice:
/// client-side encryption via a custom codec).
/// </summary>
/// <remarks>
/// Format: metadata <c>encoding=binary/encrypted</c> and <c>encryption-key-id=&lt;id&gt;</c>; data is
/// <c>nonce(12) | ciphertext | tag(16)</c> of the serialized original <see cref="Payload"/>. Random 96-bit
/// nonces are safe far beyond this app's volume (NIST bound: 2^32 messages per key). The first key
/// encrypts and every configured key can decrypt, which allows rotation without breaking running workflows.
/// Payloads that were not encrypted by this codec are rejected rather than passed through: Temporal Cloud is
/// outside the trust boundary, so anyone with namespace access but without the payload key must not be able
/// to inject plaintext workflow or activity inputs.
/// </remarks>
public sealed class AesGcmPayloadCodec : IPayloadCodec
{
    /// <summary>Metadata encoding value marking encrypted payloads.</summary>
    public const string EncryptedEncoding = "binary/encrypted";

    private const string EncodingKey = "encoding";
    private const string KeyIdKey = "encryption-key-id";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly ByteString EncryptedEncodingValue = ByteString.CopyFromUtf8(EncryptedEncoding);

    private readonly string _activeKeyId;
    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="AesGcmPayloadCodec"/> class.</summary>
    /// <param name="keys">256-bit keys; the first one encrypts.</param>
    public AesGcmPayloadCodec(IReadOnlyList<byte[]> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0 || keys.Any(k => k.Length != 32))
        {
            throw new ArgumentException("At least one 32-byte payload key is required.", nameof(keys));
        }

        foreach (var key in keys)
        {
            _keys[KeyId(key)] = key;
        }

        _activeKeyId = KeyId(keys[0]);
    }

    private static ReadOnlySpan<byte> AssociatedData => "photo-manager/temporal-payload/v1"u8;

    /// <inheritdoc />
    public Task<IReadOnlyCollection<Payload>> EncodeAsync(IReadOnlyCollection<Payload> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        return Task.FromResult<IReadOnlyCollection<Payload>>(payloads.Select(Encrypt).ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<Payload>> DecodeAsync(IReadOnlyCollection<Payload> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        return Task.FromResult<IReadOnlyCollection<Payload>>(payloads.Select(Decrypt).ToList());
    }

    private static string KeyId(byte[] key)
    {
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(key, "photo-manager/temporal-payload-key-id"u8, hash);
        return Convert.ToHexStringLower(hash[..8]);
    }

    private Payload Encrypt(Payload payload)
    {
        var plaintext = payload.ToByteArray();
        var sealedData = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = sealedData.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using (var gcm = new AesGcm(_keys[_activeKeyId], TagSize))
        {
            gcm.Encrypt(nonce, plaintext, sealedData.AsSpan(NonceSize, plaintext.Length), sealedData.AsSpan(NonceSize + plaintext.Length), AssociatedData);
        }

        return new Payload
        {
            Metadata =
            {
                [EncodingKey] = EncryptedEncodingValue,
                [KeyIdKey] = ByteString.CopyFromUtf8(_activeKeyId),
            },
            Data = ByteString.CopyFrom(sealedData),
        };
    }

    private Payload Decrypt(Payload payload)
    {
        if (!payload.Metadata.TryGetValue(EncodingKey, out var encoding) || encoding != EncryptedEncodingValue)
        {
            throw new InvalidOperationException("Rejected an unencrypted Temporal payload; every payload must be encrypted with the payload key.");
        }

        if (!payload.Metadata.TryGetValue(KeyIdKey, out var keyIdBytes) || !_keys.TryGetValue(keyIdBytes.ToStringUtf8(), out var key))
        {
            throw new InvalidOperationException("Payload was encrypted with an unknown key; check Temporal:PayloadKey/PayloadKeyPrevious.");
        }

        var sealedData = payload.Data.Span;
        if (sealedData.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("Encrypted payload is truncated.");
        }

        var plaintext = new byte[sealedData.Length - NonceSize - TagSize];
        try
        {
            using (var gcm = new AesGcm(key, TagSize))
            {
                gcm.Decrypt(sealedData[..NonceSize], sealedData.Slice(NonceSize, plaintext.Length), sealedData[^TagSize..], plaintext, AssociatedData);
            }

            return Payload.Parser.ParseFrom(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
