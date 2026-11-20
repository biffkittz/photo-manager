using System.Security.Cryptography;
using Google.Protobuf;
using PhotoManager.Infrastructure.Temporal;
using Temporalio.Api.Common.V1;

namespace PhotoManager.Worker.Tests.Infrastructure;

public sealed class AesGcmPayloadCodecTests
{
    private static Payload Plain(string json) => new()
    {
        Metadata = { ["encoding"] = ByteString.CopyFromUtf8("json/plain") },
        Data = ByteString.CopyFromUtf8(json),
    };

    [Fact]
    public async Task Encrypts_payloads_so_Temporal_never_sees_plaintext()
    {
        var codec = new AesGcmPayloadCodec([RandomNumberGenerator.GetBytes(32)]);
        var encoded = (await codec.EncodeAsync([Plain("{\"fileName\":\"beach day.jpg\"}")])).Single();

        Assert.Equal(AesGcmPayloadCodec.EncryptedEncoding, encoded.Metadata["encoding"].ToStringUtf8());
        Assert.DoesNotContain("beach", encoded.Data.ToStringUtf8(), StringComparison.Ordinal);

        var decoded = (await codec.DecodeAsync([encoded])).Single();
        Assert.Equal("{\"fileName\":\"beach day.jpg\"}", decoded.Data.ToStringUtf8());
        Assert.Equal("json/plain", decoded.Metadata["encoding"].ToStringUtf8());
    }

    [Fact]
    public async Task Previous_key_still_decrypts_after_rotation()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var encodedWithOld = (await new AesGcmPayloadCodec([oldKey]).EncodeAsync([Plain("\"x\"")])).Single();

        var rotated = new AesGcmPayloadCodec([RandomNumberGenerator.GetBytes(32), oldKey]);
        Assert.Equal("\"x\"", (await rotated.DecodeAsync([encodedWithOld])).Single().Data.ToStringUtf8());
    }

    [Fact]
    public async Task Unknown_key_and_tampering_are_rejected()
    {
        var encoded = (await new AesGcmPayloadCodec([RandomNumberGenerator.GetBytes(32)]).EncodeAsync([Plain("\"x\"")])).Single();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AesGcmPayloadCodec([RandomNumberGenerator.GetBytes(32)]).DecodeAsync([encoded]));

        var codec = new AesGcmPayloadCodec([RandomNumberGenerator.GetBytes(32)]);
        var valid = (await codec.EncodeAsync([Plain("\"y\"")])).Single();
        var bytes = valid.Data.ToByteArray();
        bytes[^1] ^= 1;
        var tampered = new Payload(valid) { Data = ByteString.CopyFrom(bytes) };
        await Assert.ThrowsAnyAsync<CryptographicException>(() => codec.DecodeAsync([tampered]));
    }

    [Fact]
    public async Task Rejects_payloads_it_did_not_encrypt()
    {
        // Someone with Temporal namespace access but without the payload key must not be able to inject inputs.
        var codec = new AesGcmPayloadCodec([RandomNumberGenerator.GetBytes(32)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => codec.DecodeAsync([Plain("\"z\"")]));
    }
}
