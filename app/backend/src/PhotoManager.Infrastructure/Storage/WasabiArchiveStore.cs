using System.Net;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using PhotoManager.Infrastructure.Configuration;

namespace PhotoManager.Infrastructure.Storage;

/// <summary><see cref="IArchiveStore"/> for Wasabi (or any S3-compatible store) using the AWS SDK.</summary>
public sealed class WasabiArchiveStore : IArchiveStore, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    /// <summary>Initializes a new instance of the <see cref="WasabiArchiveStore"/> class.</summary>
    /// <param name="options">Archive options.</param>
    public WasabiArchiveStore(IOptions<ArchiveStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value;
        _bucket = settings.BucketName;

        var config = new AmazonS3Config
        {
            ServiceURL = settings.ServiceUrl,
            AuthenticationRegion = settings.Region,
            ForcePathStyle = settings.ForcePathStyle,
            Timeout = TimeSpan.FromMinutes(5),
            MaxErrorRetry = 3,

            // Since early 2025 AWS SDKs add CRC checksums (and aws-chunked trailers) to every request by
            // default. S3-compatible providers support this unevenly, so checksums are only sent where the
            // S3 API requires them. Integrity is still enforced: uploads carry Content-MD5, and v2 files
            // are authenticated end to end by AES-GCM.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        _client = new AmazonS3Client(new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey), config);
    }

    /// <inheritdoc />
    public async Task<ArchiveListPage> ListAsync(string? continuationToken, int maxKeys, CancellationToken cancellationToken)
    {
        var response = await _client.ListObjectsV2Async(
            new ListObjectsV2Request
            {
                BucketName = _bucket,
                ContinuationToken = continuationToken,
                MaxKeys = Math.Clamp(maxKeys, 1, 1000),
            },
            cancellationToken).ConfigureAwait(false);

        // SDK v4 returns null (not empty) collections when the page is empty.
        var objects = (response.S3Objects ?? [])
            .Select(o => new ArchiveObjectInfo(
                o.Key,
                o.Size ?? 0,
                NormalizeETag(o.ETag),
                o.LastModified is { } modified ? new DateTimeOffset(modified.ToUniversalTime()) : DateTimeOffset.MinValue))
            .ToList();

        return new ArchiveListPage(objects, response.IsTruncated == true ? response.NextContinuationToken : null);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var response = await GetAsync(new GetObjectRequest { BucketName = _bucket, Key = key }, cancellationToken).ConfigureAwait(false);
        return new OwnedResponseStream(response);
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadRangeAsync(string key, long offset, long length, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        var response = await GetAsync(
            new GetObjectRequest { BucketName = _bucket, Key = key, ByteRange = new ByteRange(offset, offset + length - 1) },
            cancellationToken).ConfigureAwait(false);
        return new OwnedResponseStream(response);
    }

    /// <inheritdoc />
    public async Task<ArchiveObjectInfo?> GetInfoAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetObjectMetadataAsync(_bucket, key, cancellationToken).ConfigureAwait(false);
            return new ArchiveObjectInfo(
                key,
                response.ContentLength,
                NormalizeETag(response.ETag),
                response.LastModified is { } modified ? new DateTimeOffset(modified.ToUniversalTime()) : DateTimeOffset.MinValue);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ArchiveObjectInfo> UploadFileAsync(string key, string filePath, CancellationToken cancellationToken)
    {
        string md5;
        var stream = File.OpenRead(filePath);
        await using (stream.ConfigureAwait(false))
        {
#pragma warning disable CA5351 // MD5 is the S3 Content-MD5 transfer-integrity check, not a security control.
            md5 = Convert.ToBase64String(await MD5.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
#pragma warning restore CA5351
        }

        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                FilePath = filePath,
                ContentType = "application/octet-stream",
                MD5Digest = md5,
                UseChunkEncoding = false,
            },
            cancellationToken).ConfigureAwait(false);

        return await GetInfoAsync(key, cancellationToken).ConfigureAwait(false)
            ?? throw new StorageObjectNotFoundException($"Archive object {key} is missing immediately after upload.");
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private static string NormalizeETag(string? etag) => (etag ?? string.Empty).Trim('"');

    private async Task<GetObjectResponse> GetAsync(GetObjectRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new StorageObjectNotFoundException($"Archive object {request.Key} was not found.", ex);
        }
    }

    /// <summary>Exposes the response body and disposes the HTTP response with it.</summary>
    private sealed class OwnedResponseStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => response.ContentLength;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
