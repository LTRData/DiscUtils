using DiscUtils.Streams;

namespace DiscUtils.ReFs.Entries.Streams;

/// <summary>
///     Read-only, seekable stream over one data stream of a file. Holds what every kind of file stream shares (position, length,
///     seeking, the read loop, the members a read-only stream refuses); a subclass supplies only <see cref="ReadAt" />.
///     Not thread-safe.
/// </summary>
internal abstract class FileStreamBase(long length) : SparseStream
{
    private bool _disposed;
    private long _position;

    public override bool CanRead => !_disposed;

    public override bool CanSeek => !_disposed;

    public override bool CanWrite => false;

    // Holes inside the stream read as zeros; the whole range is reported as data, which is conservative.
    public override IEnumerable<StreamExtent> Extents
    {
        get
        {
            ThrowIfDisposed();
            return length == 0 ? [] : [new StreamExtent(0, length)];
        }
    }

    public override long Length
    {
        get
        {
            ThrowIfDisposed();
            return length;
        }
    }

    public override long Position
    {
        get
        {
            ThrowIfDisposed();
            return _position;
        }

        set
        {
            ThrowIfDisposed();

            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (offset < 0 || count < 0 || offset > buffer.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();

        var total = 0;

        while (!buffer.IsEmpty && _position < length)
        {
            var wanted = (int)Math.Min(buffer.Length, length - _position);
            var read = ReadAt(_position, buffer.Slice(0, wanted));

            if (read <= 0)
            {
                break;
            }

            total += read;
            buffer = buffer.Slice(read);
            _position += read;
        }

        return total;
    }

    // Reads complete synchronously: the data is already behind a random-access source.
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
        }

        return new ValueTask<int>(Read(buffer.Span));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (offset < 0 || count < 0 || offset > buffer.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<int>(cancellationToken);
        }

        return Task.FromResult(Read(buffer.AsSpan(offset, count)));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();

        long target;

        try
        {
            target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Cannot seek outside the addressable range of the stream.");
        }

        if (target < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        _position = target;
        return target;
    }

    public override void Flush()
    {
        // Read-only stream.
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    /// <summary>
    ///     Reads at least one byte (at most <paramref name="destination" />.Length) at <paramref name="position" />, which is
    ///     always below the stream's length; the caller advances the position by the result.
    /// </summary>
    protected abstract int ReadAt(long position, Span<byte> destination);
}