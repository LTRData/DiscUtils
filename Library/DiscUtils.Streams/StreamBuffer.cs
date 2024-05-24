//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Streams;

/// <summary>
/// Converts a Stream into an IBuffer instance.
/// </summary>
public sealed class StreamBuffer : Buffer
{
    private readonly Ownership _ownership;
    private SparseStream _stream;

    /// <summary>
    /// Initializes a new instance of the StreamBuffer class.
    /// </summary>
    /// <param name="stream">The stream to wrap.</param>
    /// <param name="ownership">Whether to dispose stream, when this object is disposed.</param>
    public StreamBuffer(Stream stream, Ownership ownership)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }
#endif

        _stream = stream as SparseStream;
        if (_stream == null)
        {
            _stream = SparseStream.FromStream(stream, ownership);
            _ownership = Ownership.Dispose;
        }
        else
        {
            _ownership = ownership;
        }
    }

    /// <summary>
    /// Can this buffer be read.
    /// </summary>
    public override bool CanRead => _stream.CanRead;

    /// <summary>
    /// Can this buffer be written.
    /// </summary>
    public override bool CanWrite => _stream.CanWrite;

    /// <summary>
    /// Gets the current capacity of the buffer, in bytes.
    /// </summary>
    public override long Capacity => _stream.Length;

    /// <summary>
    /// Gets the parts of the stream that are stored.
    /// </summary>
    /// <remarks>This may be an empty enumeration if all bytes are zero.</remarks>
    public override IEnumerable<StreamExtent> Extents => _stream.Extents;

    /// <summary>
    /// Disposes of this instance.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownership == Ownership.Dispose)
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
        }
    }

    /// <summary>
    /// Reads from the buffer into a byte array.
    /// </summary>
    /// <param name="pos">The offset within the buffer to start reading.</param>
    /// <param name="buffer">The destination byte array.</param>
    /// <param name="offset">The start offset within the destination buffer.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The actual number of bytes read.</returns>
    public override int Read(long pos, byte[] buffer, int offset, int count)
    {
        _stream.Position = pos;
        return _stream.Read(buffer, offset, count);
    }

    /// <summary>
    /// Reads from the buffer into a byte array.
    /// </summary>
    /// <param name="pos">The offset within the buffer to start reading.</param>
    /// <param name="buffer">The destination byte array.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The actual number of bytes read.</returns>
    public override ValueTask<int> ReadAsync(long pos, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        _stream.Position = pos;
        return _stream.ReadAsync(buffer, cancellationToken);
    }

    /// <summary>
    /// Reads from the buffer into a byte array.
    /// </summary>
    /// <param name="pos">The offset within the buffer to start reading.</param>
    /// <param name="buffer">The destination byte array.</param>
    /// <returns>The actual number of bytes read.</returns>
    public override int Read(long pos, Span<byte> buffer)
    {
        _stream.Position = pos;
        return _stream.Read(buffer);
    }

    /// <summary>
    /// Writes a byte array into the buffer.
    /// </summary>
    /// <param name="pos">The start offset within the buffer.</param>
    /// <param name="buffer">The source byte array.</param>
    /// <param name="offset">The start offset within the source byte array.</param>
    /// <param name="count">The number of bytes to write.</param>
    public override void Write(long pos, byte[] buffer, int offset, int count)
    {
        _stream.Position = pos;
        _stream.Write(buffer, offset, count);
    }

    /// <summary>
    /// Writes a byte array into the buffer.
    /// </summary>
    /// <param name="pos">The start offset within the buffer.</param>
    /// <param name="buffer">The source byte array.</param>
    /// <param name="cancellationToken"></param>
    public override ValueTask WriteAsync(long pos, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        _stream.Position = pos;
        return _stream.WriteAsync(buffer, cancellationToken);
    }

    /// <summary>
    /// Writes a byte array into the buffer.
    /// </summary>
    /// <param name="pos">The start offset within the buffer.</param>
    /// <param name="buffer">The source byte array.</param>
    public override void Write(long pos, ReadOnlySpan<byte> buffer)
    {
        _stream.Position = pos;
        _stream.Write(buffer);
    }

    /// <summary>
    /// Flushes all data to the underlying storage.
    /// </summary>
    public override void Flush()
    {
        _stream.Flush();
    }

    /// <summary>
    /// Sets the capacity of the buffer, truncating if appropriate.
    /// </summary>
    /// <param name="value">The desired capacity of the buffer.</param>
    public override void SetCapacity(long value)
    {
        _stream.SetLength(value);
    }

    /// <summary>
    /// Gets the parts of a buffer that are stored, within a specified range.
    /// </summary>
    /// <param name="start">The offset of the first byte of interest.</param>
    /// <param name="count">The number of bytes of interest.</param>
    /// <returns>An enumeration of stream extents, indicating stored bytes.</returns>
    public override IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
    {
        return _stream.GetExtentsInRange(start, count);
    }
}