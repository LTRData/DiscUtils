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

namespace DiscUtils.Streams;

/// <summary>
/// A sparse in-memory buffer.
/// </summary>
/// <remarks>This class is useful for storing large sparse buffers in memory, unused
/// chunks of the buffer are not stored (assumed to be zero).</remarks>
public sealed class SparseMemoryBuffer : Buffer
{
    private readonly Dictionary<int, byte[]> _buffers;

    private long _capacity;

    /// <summary>
    /// Initializes a new instance of the SparseMemoryBuffer class.
    /// </summary>
    /// <param name="chunkSize">The size of each allocation chunk.</param>
    public SparseMemoryBuffer(int chunkSize)
    {
        ChunkSize = chunkSize;
        _buffers = new Dictionary<int, byte[]>();
    }

    /// <summary>
    /// Gets the (sorted) list of allocated chunks, as chunk indexes.
    /// </summary>
    /// <returns>An enumeration of chunk indexes.</returns>
    /// <remarks>This method returns chunks as an index rather than absolute stream position.
    /// For example, if ChunkSize is 16KB, and the first 32KB of the buffer is actually stored,
    /// this method will return 0 and 1.  This indicates the first and second chunks are stored.</remarks>
    public IEnumerable<int> AllocatedChunks
    {
        get
        {
            var keys = new List<int>(_buffers.Keys);
            keys.Sort();
            return keys;
        }
    }

    /// <summary>
    /// Indicates this stream can be read (always <c>true</c>).
    /// </summary>
    public override bool CanRead => true;

    /// <summary>
    /// Indicates this stream can be written (always <c>true</c>).
    /// </summary>
    public override bool CanWrite => true;

    /// <summary>
    /// Gets the current capacity of the sparse buffer (number of logical bytes stored).
    /// </summary>
    public override long Capacity => _capacity;

    /// <summary>
    /// Gets the size of each allocation chunk.
    /// </summary>
    public int ChunkSize { get; }

    /// <summary>
    /// Accesses this memory buffer as an infinite byte array.
    /// </summary>
    /// <param name="pos">The buffer position to read.</param>
    /// <returns>The byte stored at this position (or Zero if not explicitly stored).</returns>
    public byte this[long pos]
    {
        get
        {
            Span<byte> buffer = stackalloc byte[1];
            if (Read(pos, buffer) != 0)
            {
                return buffer[0];
            }

            return 0;
        }

        set
        {
            ReadOnlySpan<byte> buffer = stackalloc byte[1] { value };
            Write(pos, buffer);
        }
    }

    /// <summary>
    /// Reads a section of the sparse buffer into a byte array.
    /// </summary>
    /// <param name="pos">The offset within the sparse buffer to start reading.</param>
    /// <param name="buffer">The destination byte array.</param>
    /// <param name="offset">The start offset within the destination buffer.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The actual number of bytes read.</returns>
    public override int Read(long pos, byte[] buffer, int offset, int count)
    {
        var totalRead = 0;

        while (count > 0 && pos < _capacity)
        {
            var chunk = (int)(pos / ChunkSize);
            var chunkOffset = (int)(pos % ChunkSize);
            var numToRead = (int)Math.Min(Math.Min(ChunkSize - chunkOffset, _capacity - pos), count);

            if (!_buffers.TryGetValue(chunk, out var chunkBuffer))
            {
                Array.Clear(buffer, offset, numToRead);
            }
            else
            {
                System.Buffer.BlockCopy(chunkBuffer, chunkOffset, buffer, offset, numToRead);
            }

            totalRead += numToRead;
            offset += numToRead;
            count -= numToRead;
            pos += numToRead;
        }

        return totalRead;
    }

    /// <summary>
    /// Reads a section of the sparse buffer into a byte array.
    /// </summary>
    /// <param name="pos">The offset within the sparse buffer to start reading.</param>
    /// <param name="buffer">The destination byte array.</param>
    /// <returns>The actual number of bytes read.</returns>
    public override int Read(long pos, Span<byte> buffer)
    {
        var totalRead = 0;

        while (buffer.Length > 0 && pos < _capacity)
        {
            var chunk = (int)(pos / ChunkSize);
            var chunkOffset = (int)(pos % ChunkSize);
            var numToRead = (int)Math.Min(Math.Min(ChunkSize - chunkOffset, _capacity - pos), buffer.Length);

            if (!_buffers.TryGetValue(chunk, out var chunkBuffer))
            {
                buffer.Slice(0, numToRead).Clear();
            }
            else
            {
                chunkBuffer.AsSpan(chunkOffset, numToRead).CopyTo(buffer);
            }

            totalRead += numToRead;
            buffer = buffer.Slice(numToRead);
            pos += numToRead;
        }

        return totalRead;
    }

    /// <summary>
    /// Writes a byte array into the sparse buffer.
    /// </summary>
    /// <param name="pos">The start offset within the sparse buffer.</param>
    /// <param name="buffer">The source byte array.</param>
    /// <param name="offset">The start offset within the source byte array.</param>
    /// <param name="count">The number of bytes to write.</param>
    public override void Write(long pos, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            var chunk = (int)(pos / ChunkSize);
            var chunkOffset = (int)(pos % ChunkSize);
            var numToWrite = Math.Min(ChunkSize - chunkOffset, count);

            if (!_buffers.TryGetValue(chunk, out var chunkBuffer))
            {
                chunkBuffer = new byte[ChunkSize];
                _buffers[chunk] = chunkBuffer;
            }

            System.Buffer.BlockCopy(buffer, offset, chunkBuffer, chunkOffset, numToWrite);

            offset += numToWrite;
            count -= numToWrite;
            pos += numToWrite;
        }

        _capacity = Math.Max(_capacity, pos);
    }

    public override void Write(long pos, ReadOnlySpan<byte> buffer)
    {
        while (buffer.Length > 0)
        {
            var chunk = (int)(pos / ChunkSize);
            var chunkOffset = (int)(pos % ChunkSize);
            var numToWrite = Math.Min(ChunkSize - chunkOffset, buffer.Length);

            if (!_buffers.TryGetValue(chunk, out var chunkBuffer))
            {
                chunkBuffer = new byte[ChunkSize];
                _buffers[chunk] = chunkBuffer;
            }

            buffer.Slice(0, numToWrite).CopyTo(chunkBuffer.AsSpan(chunkOffset));

            buffer = buffer.Slice(numToWrite);

            pos += numToWrite;
        }

        _capacity = Math.Max(_capacity, pos);
    }

    /// <summary>
    /// Clears bytes from the buffer.
    /// </summary>
    /// <param name="pos">The start offset within the buffer.</param>
    /// <param name="count">The number of bytes to clear.</param>
    public override void Clear(long pos, int count)
    {
        while (count > 0)
        {
            var chunk = (int)(pos / ChunkSize);
            var chunkOffset = (int)(pos % ChunkSize);
            var numToClear = Math.Min(ChunkSize - chunkOffset, count);

            if (_buffers.TryGetValue(chunk, out var chunkBuffer))
            {
                if (chunkOffset == 0 && numToClear == ChunkSize)
                {
                    _buffers.Remove(chunk);
                }
                else
                {
                    Array.Clear(chunkBuffer, chunkOffset, numToClear);
                }
            }

            count -= numToClear;
            pos += numToClear;
        }

        _capacity = Math.Max(_capacity, pos);
    }

    /// <summary>
    /// Sets the capacity of the sparse buffer, truncating if appropriate.
    /// </summary>
    /// <param name="value">The desired capacity of the buffer.</param>
    /// <remarks>This method does not allocate any chunks, it merely records the logical
    /// capacity of the sparse buffer.  Writes beyond the specified capacity will increase
    /// the capacity.</remarks>
    public override void SetCapacity(long value)
    {
        _capacity = value;
    }

    /// <summary>
    /// Gets the parts of a buffer that are stored, within a specified range.
    /// </summary>
    /// <param name="start">The offset of the first byte of interest.</param>
    /// <param name="count">The number of bytes of interest.</param>
    /// <returns>An enumeration of stream extents, indicating stored bytes.</returns>
    public override IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
    {
        var end = start + count;
        foreach (var chunk in AllocatedChunks)
        {
            var chunkStart = chunk * (long)ChunkSize;
            var chunkEnd = chunkStart + ChunkSize;
            if (chunkEnd > start && chunkStart < end)
            {
                var extentStart = Math.Max(start, chunkStart);
                yield return new StreamExtent(extentStart, Math.Min(chunkEnd, end) - extentStart);
            }
        }
    }

    /// <summary>
    /// Writes from a stream into the sparse buffer.
    /// </summary>
    /// <param name="pos">The start offset within the sparse buffer.</param>
    /// <param name="source">The stream to get data from.</param>
    /// <param name="count">The number of bytes to write.</param>
    public long WriteFromStream(long pos, Stream source, long count)
    {
        long totalWritten = 0;

        while (totalWritten < count)
        {
            var chunk = (int)(pos / ChunkSize);
            var chunkOffset = (int)(pos % ChunkSize);
            var numToWrite = (int)Math.Min(ChunkSize - chunkOffset, count - totalWritten);

            if (!_buffers.TryGetValue(chunk, out var chunkBuffer))
            {
                chunkBuffer = new byte[ChunkSize];
                _buffers[chunk] = chunkBuffer;
            }

            var numRead = source.Read(chunkBuffer, chunkOffset, numToWrite);

            if (numRead <= 0)
            {
                break;
            }

            totalWritten += numRead;
            pos += numRead;

            if (numRead < numToWrite)
            {
                break;
            }
        }

        _capacity = Math.Max(_capacity, pos);

        return totalWritten;
    }

}