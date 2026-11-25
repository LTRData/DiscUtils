using DiscUtils.Streams;
using DiscUtils.DeduplicationRole.ChunkStore;

namespace DiscUtils.DeduplicationRole.Streams;

/// <summary>
/// The content of a file optimized by the Data Deduplication role, as Windows reads it through the dedup filter: the ranges the file's own data stream
/// holds (written after the optimization; the stub's <see cref="SparseStream.Extents"/>) come from the file; the rest of the ranges the reparse point
/// names come from the chunk store; anything else is the file's own stream (holes read as zeros). A stub that is not a <see cref="SparseStream"/> is
/// taken to hold nothing. Recall bitmaps are not supported. Not thread-safe.
/// </summary>
internal sealed class DedupFileStream : SparseStream.ReadOnlySparseStream
{
    private readonly DedupChunk[] _chunks;
    private readonly StreamExtent[] _own;
    private readonly Stream _stub;
    private readonly DedupChunkStore _store;
    private byte[]? _cached;
    private int _cachedIndex = -1;
    private long _position;
    private bool _disposed;

    public DedupFileStream(Stream stub, DedupChunkStore store, IReadOnlyList<DedupChunk> chunks, long length)
    {
        _stub = stub;
        _store = store;
        _chunks = [.. chunks];
        _own = stub is SparseStream sparse ? [.. sparse.Extents.Where(e => e.Length > 0).OrderBy(e => e.Start)] : [];
        Length = length;
    }

    public override bool CanRead => !_disposed;

    public override bool CanSeek => !_disposed;

    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override IEnumerable<StreamExtent> Extents => Length == 0 ? [] : [new StreamExtent(0, Length)];

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DedupFileStream));
        }

        var total = 0;
        while (total < buffer.Length && _position < Length)
        {
            var read = ReadAt(_position, buffer.Slice(total, (int)Math.Min(buffer.Length - total, Length - _position)));
            if (read <= 0)
            {
                break;
            }

            total += read;
            _position += read;
        }

        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => offset
        };
        return _position;
    }

    private int ReadAt(long position, Span<byte> destination)
    {
        // The stub's own data first: the ranges written since the optimization.
        var (owned, end) = OwnAt(position);
        var wanted = (int)Math.Min(destination.Length, end - position);
        var index = owned ? -1 : FindChunk(position);

        if (index < 0)
        {
            // Written since the optimization, or past the ranges (the ranges start at 0 and are contiguous): the file's own bytes.
            _stub.Position = position;
            var read = _stub.ReadMaximum(destination.Slice(0, wanted));
            if (read < wanted)
            {
                destination.Slice(read, wanted - read).Clear();
            }

            return wanted;
        }

        var chunk = _chunks[index];
        if (_cachedIndex != index)
        {
            _cached = _store.ReadChunk(chunk);
            _cachedIndex = index;
        }

        var offset = (int)(position - (long)chunk.Start);
        var count = Math.Min(wanted, chunk.Length - offset);
        _cached.AsSpan(offset, count).CopyTo(destination);
        return count;
    }

    /// <summary>Whether <paramref name="position"/> lies in data the stub holds, and the byte where that changes.</summary>
    private (bool Owned, long End) OwnAt(long position)
    {
        int low = 0, high = _own.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var extent = _own[middle];
            if (extent.Start + extent.Length <= position)
            {
                low = middle + 1;
            }
            else if (extent.Start > position)
            {
                high = middle - 1;
            }
            else
            {
                return (true, extent.Start + extent.Length);
            }
        }

        return (false, low < _own.Length ? _own[low].Start : long.MaxValue);
    }

    private int FindChunk(long position)
    {
        var low = 0;
        var high = _chunks.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if ((long)_chunks[middle].End <= position)
            {
                low = middle + 1;
            }
            else if ((long)_chunks[middle].Start > position)
            {
                high = middle - 1;
            }
            else
            {
                return middle;
            }
        }

        return -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _stub.Dispose();
        }

        _disposed = true;
        _cached = null;
        base.Dispose(disposing);
    }
}
