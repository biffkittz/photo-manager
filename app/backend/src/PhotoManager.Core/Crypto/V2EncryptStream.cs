using System.Security.Cryptography;

namespace PhotoManager.Core.Crypto;

/// <summary>
/// Write-only stream that encrypts plaintext into v2 content chunks. Obtain one from
/// <see cref="PhotoCipherV2.CreateEncryptStreamAsync"/>, write the plaintext, then call
/// <see cref="CompleteAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// A full chunk is only sealed as non-final once at least one more byte arrives; whatever is buffered
/// when <see cref="CompleteAsync"/> runs (possibly a full chunk, possibly nothing) becomes the final
/// chunk. This is what lets the decryptor detect truncation.
/// </para>
/// <para>
/// Disposing without completing deliberately does <b>not</b> write a final chunk. If the source failed
/// halfway through, the output is left unreadable instead of being a valid-looking truncated file.
/// </para>
/// </remarks>
public sealed class V2EncryptStream : Stream
{
    private readonly Stream _destination;
    private readonly bool _leaveOpen;
    private readonly byte[] _contentKey;
    private readonly AesGcm _gcm;
    private readonly byte[] _plain;
    private readonly byte[] _cipher;
    private readonly int _chunkSize;
    private int _filled;
    private long _counter;
    private bool _completed;
    private bool _disposed;

    internal V2EncryptStream(Stream destination, byte[] contentKey, int chunkSize, bool leaveOpen)
    {
        _destination = destination;
        _leaveOpen = leaveOpen;
        _contentKey = contentKey;
        _chunkSize = chunkSize;
        _gcm = new AesGcm(contentKey, V2Format.TagSize);
        _plain = new byte[chunkSize];
        _cipher = new byte[chunkSize + V2Format.TagSize];
    }

    /// <summary>Gets the number of plaintext bytes written so far.</summary>
    public long PlaintextLength { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => !_completed && !_disposed;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureWritable();
        while (!buffer.IsEmpty)
        {
            if (_filled == _chunkSize)
            {
                var sealedLength = SealChunk(isFinal: false);
                _destination.Write(_cipher, 0, sealedLength);
            }

            buffer = BufferPlaintext(buffer);
        }
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        while (!buffer.IsEmpty)
        {
            if (_filled == _chunkSize)
            {
                var sealedLength = SealChunk(isFinal: false);
                await _destination.WriteAsync(_cipher.AsMemory(0, sealedLength), cancellationToken).ConfigureAwait(false);
            }

            var take = Math.Min(_chunkSize - _filled, buffer.Length);
            buffer.Span[..take].CopyTo(_plain.AsSpan(_filled));
            _filled += take;
            PlaintextLength += take;
            buffer = buffer[take..];
        }
    }

    /// <summary>Seals the final chunk and flushes the destination. Must be called exactly once.</summary>
    /// <param name="cancellationToken">Cancels the final write.</param>
    /// <returns>A task that completes when the final chunk has been written.</returns>
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        var sealedLength = SealChunk(isFinal: true);
        await _destination.WriteAsync(_cipher.AsMemory(0, sealedLength), cancellationToken).ConfigureAwait(false);
        await _destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    /// <summary>Synchronous counterpart of <see cref="CompleteAsync"/>.</summary>
    public void Complete()
    {
        EnsureWritable();
        var sealedLength = SealChunk(isFinal: true);
        _destination.Write(_cipher, 0, sealedLength);
        _destination.Flush();
        _completed = true;
    }

    /// <summary>No-op: a partial chunk cannot be flushed without ending the stream.</summary>
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            ReleaseCryptoState();
            if (!_leaveOpen)
            {
                await _destination.DisposeAsync().ConfigureAwait(false);
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
                _destination.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private ReadOnlySpan<byte> BufferPlaintext(ReadOnlySpan<byte> buffer)
    {
        var take = Math.Min(_chunkSize - _filled, buffer.Length);
        buffer[..take].CopyTo(_plain.AsSpan(_filled));
        _filled += take;
        PlaintextLength += take;
        return buffer[take..];
    }

    private int SealChunk(bool isFinal)
    {
        Span<byte> nonce = stackalloc byte[V2Format.NonceSize];
        V2Format.ComputeChunkNonce(_counter, isFinal, nonce);
        _gcm.Encrypt(nonce, _plain.AsSpan(0, _filled), _cipher.AsSpan(0, _filled), _cipher.AsSpan(_filled, V2Format.TagSize));
        var sealedLength = _filled + V2Format.TagSize;
        _counter++;
        _filled = 0;
        return sealedLength;
    }

    private void EnsureWritable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("The encrypted stream has already been completed.");
        }
    }

    private void ReleaseCryptoState()
    {
        _gcm.Dispose();
        CryptographicOperations.ZeroMemory(_contentKey);
        CryptographicOperations.ZeroMemory(_plain);
    }
}
