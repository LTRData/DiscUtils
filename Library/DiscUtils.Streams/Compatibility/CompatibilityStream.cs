using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LTRData.Extensions.Async;

namespace DiscUtils.Streams.Compatibility;

public abstract class CompatibilityStream : Stream
{
    /// <summary>
    /// In a derived class, get corresponding position in a base stream of this instance
    /// to a position in this instance.
    /// </summary>
    public virtual long? GetPositionInBaseStream(Stream baseStream, long virtualPosition)
    {
        if (baseStream is null
            || ReferenceEquals(baseStream, this))
        {
            return virtualPosition;
        }

        return null;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(buffer);
#else
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }
#endif

        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(buffer);
#else
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }
#endif

        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    public abstract override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    public abstract override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
    public abstract override int Read(Span<byte> buffer);
    public abstract override void Write(ReadOnlySpan<byte> buffer);
#else
    public abstract ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    public abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
    public abstract int Read(Span<byte> buffer);
    public abstract void Write(ReadOnlySpan<byte> buffer);
#endif

    public override int ReadByte()
    {
        Span<byte> b = stackalloc byte[1];
        if (Read(b) != 1)
        {
            return -1;
        }

        return b[0];
    }

    public override void WriteByte(byte value)
        => Write([value]);

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => ReadAsync(buffer, offset, count, CancellationToken.None).AsAsyncResult(callback, state);

    public override int EndRead(IAsyncResult asyncResult)
        => ((Task<int>)asyncResult).GetAwaiter().GetResult();

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => WriteAsync(buffer, offset, count, CancellationToken.None).AsAsyncResult(callback, state);

    public override void EndWrite(IAsyncResult asyncResult)
        => ((Task)asyncResult).GetAwaiter().GetResult();
}

public abstract class ReadOnlyCompatibilityStream : CompatibilityStream
{
    public sealed override bool CanWrite
        => false;
    
    public sealed override void Write(byte[] buffer, int offset, int count)
        => throw new InvalidOperationException("Attempt to write to read-only stream");
    
    public sealed override void Write(ReadOnlySpan<byte> buffer)
        => throw new InvalidOperationException("Attempt to write to read-only stream");
    
    public sealed override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Attempt to write to read-only stream");
    
    public sealed override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Attempt to write to read-only stream");
    
    public sealed override void WriteByte(byte value)
        => throw new InvalidOperationException("Attempt to write to read-only stream");
    
    public sealed override void Flush() { }
    
    public sealed override Task FlushAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
    
    public sealed override void SetLength(long value)
        => throw new InvalidOperationException("Attempt to change length of read-only stream");
}

