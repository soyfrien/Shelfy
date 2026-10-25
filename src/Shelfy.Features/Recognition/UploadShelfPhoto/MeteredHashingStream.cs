using System.Security.Cryptography;

namespace Shelfy.Features.Recognition.UploadShelfPhoto;

public class PhotoTooLargeException(long limit) : Exception($"The photo exceeds the {limit} byte limit.");

/// <summary>
/// Read-only pass-through that first replays bytes already peeked from the source, counts everything,
/// hashes it with SHA-256 and throws once more than <c>maxBytes</c> have been read. One pass, constant memory.
/// </summary>
public sealed class MeteredHashingStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private byte[] _prefix;
    private int _prefixOffset;
    private string? _hex;

    public MeteredHashingStream(Stream inner, byte[] prefix, long maxBytes)
    {
        _inner = inner;
        _prefix = prefix;
        _maxBytes = maxBytes;
    }

    public long BytesRead { get; private set; }

    /// <summary>Lowercase hex SHA-256 of everything read so far. Call after the stream is drained.</summary>
    public string Sha256Hex => _hex ??= Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read;
        if (_prefixOffset < _prefix.Length)
        {
            read = Math.Min(buffer.Length, _prefix.Length - _prefixOffset);
            _prefix.AsMemory(_prefixOffset, read).CopyTo(buffer);
            _prefixOffset += read;
        }
        else
        {
            read = await _inner.ReadAsync(buffer, cancellationToken);
        }

        Account(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int read;
        if (_prefixOffset < _prefix.Length)
        {
            read = Math.Min(buffer.Length, _prefix.Length - _prefixOffset);
            _prefix.AsSpan(_prefixOffset, read).CopyTo(buffer);
            _prefixOffset += read;
        }
        else
        {
            read = _inner.Read(buffer);
        }

        Account(buffer[..read]);
        return read;
    }

    private void Account(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        BytesRead += data.Length;
        if (BytesRead > _maxBytes)
        {
            throw new PhotoTooLargeException(_maxBytes);
        }

        _hash.AppendData(data);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}
