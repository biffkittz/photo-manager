using System.Security.Cryptography;

namespace PhotoManager.Core.Crypto;

/// <summary>
/// Read-only stream that verifies and decrypts v2 content chunks. Every chunk is authenticated before
/// any of its plaintext is returned, so a consumer never observes unauthenticated bytes. Truncation and
/// trailing data are detected when the last chunk is reached.
/// </summary>
/// <remarks>
/// To decide whether a chunk is the final one, the stream reads one chunk ahead: a chunk is final when
/// it is shorter than a full unit or when nothing follows it. The nonce's final flag then authenticates
/// that decision.
/// </remarks>
public sealed class V2DecryptStream : Stream
{
    private readonly Stream _source;
    private readonly bool _leaveOpen;
    private readonly byte[] _contentKey;
    private readonly AesGcm _gcm;
    private readonly int _unitSize;
    private readonly byte[] _plain;
    private byte[] _current;
    private byte[] _next;
    private int _currentLength;
    private int _nextLength;
    private int _plainOffset;
    private int _plainLength;
    private long _counter;
    private bool _started;
    private bool _finished;
    private bool _disposed;

    internal V2DecryptStream(Stream source, byte[] contentKey, V2Header header, EmbeddedPhotoMetadata? metadata, bool leaveOpen)
    {
        _source = source;
        _leaveOpen = leaveOpen;
        _contentKey = contentKey;
        _gcm = new AesGcm(contentKey, V2Format.TagSize);
        _unitSize = header.ChunkSize + V2Format.TagSize;
        _current = new byte[_unitSize];
        _next = new byte[_unitSize];
        _plain = new byte[header.ChunkSize];
        KeyType = header.KeyType;
        KeyIdHex = Convert.ToHexStringLower(header.KeyId);
        Metadata = metadata;
    }

    /// <summary>Gets the authenticated metadata embedded in the file, if any.</summary>
    public EmbeddedPhotoMetadata? Metadata { get; }

    /// <summary>Gets the key type recorded in the header.</summary>
    public V2KeyType KeyType { get; }

    /// <summary>Gets the key identifier recorded in the header (hex).</summary>
    public string KeyIdHex { get; }

    /// <inheritdoc />
    public override bool CanRead => !_disposed;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
        {
            return 0;
        }

        while (_plainOffset == _plainLength)
        {
            if (_finished)
            {
                return 0;
            }

            AdvanceSync();
        }

        return CopyPlaintext(buffer);
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
        {
            return 0;
        }

        while (_plainOffset == _plainLength)
        {
            if (_finished)
            {
                return 0;
            }

            await AdvanceAsync(cancellationToken).ConfigureAwait(false);
        }

        return CopyPlaintext(buffer.Span);
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            ReleaseCryptoState();
            if (!_leaveOpen)
            {
                await _source.DisposeAsync().ConfigureAwait(false);
            }
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            ReleaseCryptoState();
            if (!_leaveOpen)
            {
                _source.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private void AdvanceSync()
    {
        if (!_started)
        {
            _started = true;
            _currentLength = _source.ReadAtLeast(_current, _unitSize, throwOnEndOfStream: false);
        }
        else
        {
            (_current, _next) = (_next, _current);
            _currentLength = _nextLength;
        }

        var isFinal = _currentLength < _unitSize;
        if (!isFinal)
        {
            _nextLength = _source.ReadAtLeast(_next, _unitSize, throwOnEndOfStream: false);
            isFinal = _nextLength == 0;
        }

        DecryptCurrent(isFinal);
    }

    private async ValueTask AdvanceAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            _started = true;
            _currentLength = await _source.ReadAtLeastAsync(_current, _unitSize, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            (_current, _next) = (_next, _current);
            _currentLength = _nextLength;
        }

        var isFinal = _currentLength < _unitSize;
        if (!isFinal)
        {
            _nextLength = await _source.ReadAtLeastAsync(_next, _unitSize, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            isFinal = _nextLength == 0;
        }

        DecryptCurrent(isFinal);
    }

    private void DecryptCurrent(bool isFinal)
    {
        if (_currentLength < V2Format.TagSize)
        {
            throw new PhotoDecryptionException("Encrypted content is truncated.");
        }

        var plaintextLength = _currentLength - V2Format.TagSize;
        Span<byte> nonce = stackalloc byte[V2Format.NonceSize];
        V2Format.ComputeChunkNonce(_counter, isFinal, nonce);
        try
        {
            _gcm.Decrypt(
                nonce,
                _current.AsSpan(0, plaintextLength),
                _current.AsSpan(plaintextLength, V2Format.TagSize),
                _plain.AsSpan(0, plaintextLength));
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new PhotoDecryptionException(
                $"Authentication failed for chunk {_counter}: the file is corrupted, truncated, reordered, or was encrypted with a different key.",
                ex);
        }

        _counter++;
        _plainOffset = 0;
        _plainLength = plaintextLength;
        _finished = isFinal;
    }

    private int CopyPlaintext(Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, _plainLength - _plainOffset);
        _plain.AsSpan(_plainOffset, count).CopyTo(buffer);
        _plainOffset += count;
        return count;
    }

    private void ReleaseCryptoState()
    {
        _gcm.Dispose();
        CryptographicOperations.ZeroMemory(_contentKey);
        CryptographicOperations.ZeroMemory(_plain);
    }
}
