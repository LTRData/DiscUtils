//
// Copyright (c) 2008-2024, Kenneth Bell, Olof Lagerkvist and contributors
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

using DiscUtils.Streams.Compatibility;
using DiscUtils.Compression;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Ntfs.Internals;

internal class WofStream(long uncompressedSize,
                         int chunkOrder,
                         int maxChunkSize,
                         int numChunks,
                         int chunkTableSize,
                         long[] chunkTable,
                         Wof.CompressionFormat compressionFormat,
                         SparseStream compressedData) : SparseStream
{
    private (long offset, int size, int chunkSize) PrepareReadChunk(int chunkIndex)
    {
        long offset = chunkTableSize;
        var chunkSize = maxChunkSize;
        if (chunkIndex > 0)
        {
            offset += chunkTable[chunkIndex - 1];
        }

        int size;
        if (chunkIndex < numChunks - 1)
        {
            size = (int)(chunkTable[chunkIndex] - offset + chunkTableSize);
        }
        else
        {
            size = (int)(compressedData.Length - offset);
            var tail = (int)(Length & (chunkSize - 1));

            if (tail > 0)
            {
                chunkSize = tail;
            }
        }

        return (offset, size, chunkSize);
    }

    private int ReadChunk(Span<byte> uncompressedData, int chunkIndex)
    {
        var (offset, size, chunkSize) = PrepareReadChunk(chunkIndex);

        if (size == chunkSize)
        {
            compressedData.Position = offset;
            return compressedData.Read(uncompressedData.Slice(0, chunkSize));
        }

        var compressed = new SubStream(compressedData, offset, size);

        using var decompressStream = GetDecompressStream(chunkSize, compressed);

        return decompressStream.Read(uncompressedData.Slice(0, chunkSize));
    }

    private async ValueTask<int> ReadChunkAsync(Memory<byte> uncompressedData, int chunkIndex, CancellationToken cancellationToken)
    {
        var (offset, size, chunkSize) = PrepareReadChunk(chunkIndex);

        if (size == chunkSize)
        {
            compressedData.Position = offset;
            return await compressedData.ReadAsync(uncompressedData.Slice(0, chunkSize), cancellationToken).ConfigureAwait(false);
        }

        var compressed = new SubStream(compressedData, offset, size);

        using var decompressStream = GetDecompressStream(chunkSize, compressed);

        return await decompressStream.ReadAsync(uncompressedData.Slice(0, chunkSize), cancellationToken).ConfigureAwait(false);
    }

    private int ReadChunk(byte[] uncompressedData, int byteOffset, int chunkIndex)
    {
        var (offset, size, chunkSize) = PrepareReadChunk(chunkIndex);

        if (size == chunkSize)
        {
            compressedData.Position = offset;
            return compressedData.Read(uncompressedData, byteOffset, chunkSize);
        }

        var compressed = new SubStream(compressedData, offset, size);

        using var decompressStream = GetDecompressStream(chunkSize, compressed);

        return decompressStream.Read(uncompressedData, byteOffset, chunkSize);
    }

    private CompatibilityStream GetDecompressStream(int chunkSize, SubStream compressed)
    {
        if (compressionFormat == Wof.CompressionFormat.LZX)
        {
            return new LzxStream(compressed, windowBits: 15, chunkSize);
        }

        return new XpressStream(compressed, chunkSize);
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length { get; } = uncompressedSize;

    public override long Position { get; set; }

    public override IEnumerable<StreamExtent> Extents
        => SingleValueEnumerable.Get(new StreamExtent(0, Length));

    public override void Flush() { }

    public override int Read(Span<byte> buffer)
    {
        if ((Position & (maxChunkSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException("Read not aligned to compression chunks");
        }

        var total = 0;

        while (!buffer.IsEmpty && Position < Length)
        {
            var block = buffer.Slice(0, Math.Min(maxChunkSize, Math.Min(buffer.Length, (int)(Length - Position))));

            var length = ReadChunk(block, (int)(Position >> chunkOrder));

            total += length;
            Position += length;

            if (length != block.Length)
            {
                break;
            }

            buffer = buffer.Slice(length);
        }

        return total;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if ((Position & (maxChunkSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException("Read not aligned to compression chunks");
        }

        var total = 0;

        while (!buffer.IsEmpty && Position < Length)
        {
            var block = buffer.Slice(0, Math.Min(maxChunkSize, Math.Min(buffer.Length, (int)(Length - Position))));

            var length = await ReadChunkAsync(block, (int)(Position >> chunkOrder), cancellationToken).ConfigureAwait(false);

            total += length;
            Position += length;

            if (length != block.Length)
            {
                break;
            }

            buffer = buffer.Slice(length);
        }

        return total;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if ((Position & (maxChunkSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException("Read not aligned to compression chunks");
        }

        var total = 0;

        while (count > 0 && Position < Length)
        {
            var block = Math.Min(maxChunkSize, Math.Min(count, (int)(Length - Position)));

            var length = ReadChunk(buffer, offset, (int)(Position >> chunkOrder));

            total += length;
            Position += length;

            if (length != block)
            {
                break;
            }

            offset += length;
            count -= length;
        }

        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        switch (origin)
        {
            case SeekOrigin.Begin:
                Position = offset;
                break;

            case SeekOrigin.Current:
                Position += offset;
                break;

            case SeekOrigin.End:
                Position = Length + offset;
                break;

            default:
                throw new ArgumentException($"Invalid origin {origin}", nameof(origin));
        }

        return Position;
    }

    public override void SetLength(long value) => throw new NotImplementedException();
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotImplementedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new NotImplementedException();
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public override void WriteByte(byte value) => throw new NotImplementedException();
    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state) => throw new NotImplementedException();
    public override void EndWrite(IAsyncResult asyncResult) => throw new NotImplementedException();
}
