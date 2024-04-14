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
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Buffers;
using Buffer = DiscUtils.Streams.Buffer;

namespace DiscUtils.Ext;

internal class FileBuffer : Buffer, IFileBuffer
{
    private readonly Context _context;
    private readonly Inode _inode;

    public FileBuffer(Context context, Inode inode)
    {
        _context = context;
        _inode = inode;
    }

    public override bool CanRead => true;

    public override bool CanWrite => false;

    public override long Capacity => _inode.FileSize;

    public IEnumerable<StreamExtent> EnumerateAllocationExtents()
    {
        var pos = 0;

        if (pos >= _inode.FileSize)
        {
            yield break;
        }

        var blockSize = _context.SuperBlock.BlockSize;

        var count = _inode.FileSize;
        var totalRead = 0;
        var totalBytesRemaining = (int)Math.Min(count, _inode.FileSize - pos);

        while (totalBytesRemaining > 0)
        {
            var logicalBlock = (uint)((pos + totalRead) / blockSize);
            var blockOffset = (int)(pos + totalRead - logicalBlock * (long)blockSize);

            uint physicalBlock = 0;
            if (logicalBlock < 12)
            {
                physicalBlock = _inode.DirectBlocks[logicalBlock];
            }
            else
            {
                logicalBlock -= 12;
                if (logicalBlock < blockSize / 4)
                {
                    if (_inode.IndirectBlock != 0)
                    {
                        _context.RawStream.Position = _inode.IndirectBlock * (long)blockSize + logicalBlock * 4;
                        physicalBlock = EndianUtilities.ReadUInt32LittleEndian(_context.RawStream);
                    }
                }
                else
                {
                    logicalBlock -= blockSize / 4;
                    if (logicalBlock < blockSize / 4 * (blockSize / 4))
                    {
                        if (_inode.DoubleIndirectBlock != 0)
                        {
                            _context.RawStream.Position = _inode.DoubleIndirectBlock * (long)blockSize +
                                                          logicalBlock / (blockSize / 4) * 4;
                            var indirectBlock = EndianUtilities.ReadUInt32LittleEndian(_context.RawStream);

                            if (indirectBlock != 0)
                            {
                                _context.RawStream.Position = indirectBlock * (long)blockSize +
                                                              logicalBlock % (blockSize / 4) * 4;
                                physicalBlock = EndianUtilities.ReadUInt32LittleEndian(_context.RawStream);
                            }
                        }
                    }
                    else
                    {
                        throw new NotSupportedException("Triple indirection");
                    }
                }
            }

            var toRead = (int)Math.Min(totalBytesRemaining, blockSize - blockOffset);
            if (physicalBlock != 0)
            {
                yield return new(physicalBlock * (long)blockSize + blockOffset, toRead);
            }

            totalBytesRemaining -= toRead;
            totalRead += toRead;
        }
    }

    public IEnumerable<Range<long, long>> EnumerateAllocationClusters()
    {
        if (_inode.FileSize == 0)
        {
            yield break;
        }

        var blockSize = _context.SuperBlock.BlockSize;

        var count = _inode.BlocksCount;
        var totalRead = 0;
        var totalBlocksRemaining = count;

        while (totalBlocksRemaining > 0)
        {
            var logicalBlock = (uint)totalRead;

            uint physicalBlock = 0;
            if (logicalBlock < 12)
            {
                physicalBlock = _inode.DirectBlocks[logicalBlock];
            }
            else
            {
                logicalBlock -= 12;
                if (logicalBlock < blockSize / 4)
                {
                    if (_inode.IndirectBlock != 0)
                    {
                        _context.RawStream.Position = _inode.IndirectBlock * (long)blockSize + logicalBlock * 4;
                        physicalBlock = EndianUtilities.ReadUInt32LittleEndian(_context.RawStream);
                    }
                }
                else
                {
                    logicalBlock -= blockSize / 4;
                    if (logicalBlock < blockSize / 4 * (blockSize / 4))
                    {
                        if (_inode.DoubleIndirectBlock != 0)
                        {
                            _context.RawStream.Position = _inode.DoubleIndirectBlock * (long)blockSize +
                                                          logicalBlock / (blockSize / 4) * 4;
                            var indirectBlock = EndianUtilities.ReadUInt32LittleEndian(_context.RawStream);

                            if (indirectBlock != 0)
                            {
                                _context.RawStream.Position = indirectBlock * (long)blockSize +
                                                              logicalBlock % (blockSize / 4) * 4;
                                physicalBlock = EndianUtilities.ReadUInt32LittleEndian(_context.RawStream);
                            }
                        }
                    }
                    else
                    {
                        throw new NotSupportedException("Triple indirection");
                    }
                }
            }

            if (physicalBlock != 0)
            {
                yield return new(physicalBlock, 1);
            }

            totalBlocksRemaining --;
            totalRead ++;
        }
    }

    public override int Read(long pos, byte[] buffer, int offset, int count)
    {
        if (pos > _inode.FileSize)
        {
            return 0;
        }

        var blockSize = _context.SuperBlock.BlockSize;

        var totalRead = 0;
        var totalBytesRemaining = (int)Math.Min(count, _inode.FileSize - pos);

        Span<byte> indirectData = stackalloc byte[4];

        while (totalBytesRemaining > 0)
        {
            var logicalBlock = (uint)((pos + totalRead) / blockSize);
            var blockOffset = (int)(pos + totalRead - logicalBlock * (long)blockSize);

            uint physicalBlock = 0;
            if (logicalBlock < 12)
            {
                physicalBlock = _inode.DirectBlocks[logicalBlock];
            }
            else
            {
                logicalBlock -= 12;
                if (logicalBlock < blockSize / 4)
                {
                    if (_inode.IndirectBlock != 0)
                    {
                        _context.RawStream.Position = _inode.IndirectBlock * (long)blockSize + logicalBlock * 4;
                        _context.RawStream.ReadExactly(indirectData);
                        physicalBlock = EndianUtilities.ToUInt32LittleEndian(indirectData);
                    }
                }
                else
                {
                    logicalBlock -= blockSize / 4;
                    if (logicalBlock < blockSize / 4 * (blockSize / 4))
                    {
                        if (_inode.DoubleIndirectBlock != 0)
                        {
                            _context.RawStream.Position = _inode.DoubleIndirectBlock * (long)blockSize +
                                                          logicalBlock / (blockSize / 4) * 4;
                            _context.RawStream.ReadExactly(indirectData);
                            var indirectBlock = EndianUtilities.ToUInt32LittleEndian(indirectData);

                            if (indirectBlock != 0)
                            {
                                _context.RawStream.Position = indirectBlock * (long)blockSize +
                                                              logicalBlock % (blockSize / 4) * 4;
                                _context.RawStream.ReadExactly(indirectData);
                                physicalBlock = EndianUtilities.ToUInt32LittleEndian(indirectData);
                            }
                        }
                    }
                    else
                    {
                        throw new NotSupportedException("Triple indirection");
                    }
                }
            }

            var toRead = (int)Math.Min(totalBytesRemaining, blockSize - blockOffset);
            int numRead;
            if (physicalBlock == 0)
            {
                Array.Clear(buffer, offset + totalRead, toRead);
                numRead = toRead;
            }
            else
            {
                _context.RawStream.Position = physicalBlock * (long)blockSize + blockOffset;
                numRead = _context.RawStream.Read(buffer, offset + totalRead, toRead);
            }

            totalBytesRemaining -= numRead;
            totalRead += numRead;
        }

        return totalRead;
    }

    public override async ValueTask<int> ReadAsync(long pos, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (pos > _inode.FileSize)
        {
            return 0;
        }

        var blockSize = _context.SuperBlock.BlockSize;

        var totalRead = 0;
        var totalBytesRemaining = (int)Math.Min(buffer.Length, _inode.FileSize - pos);

        while (totalBytesRemaining > 0)
        {
            var logicalBlock = (uint)((pos + totalRead) / blockSize);
            var blockOffset = (int)(pos + totalRead - logicalBlock * (long)blockSize);

            uint physicalBlock = 0;
            if (logicalBlock < 12)
            {
                physicalBlock = _inode.DirectBlocks[logicalBlock];
            }
            else
            {
                logicalBlock -= 12;
                if (logicalBlock < blockSize / 4)
                {
                    if (_inode.IndirectBlock != 0)
                    {
                        _context.RawStream.Position = _inode.IndirectBlock * (long)blockSize + logicalBlock * 4;
                        physicalBlock = await EndianUtilities.ReadUInt32LittleEndianAsync(_context.RawStream, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    logicalBlock -= blockSize / 4;
                    if (logicalBlock < blockSize / 4 * (blockSize / 4))
                    {
                        if (_inode.DoubleIndirectBlock != 0)
                        {
                            _context.RawStream.Position = _inode.DoubleIndirectBlock * (long)blockSize +
                                                          logicalBlock / (blockSize / 4) * 4;
                            var indirectBlock = await EndianUtilities.ReadUInt32LittleEndianAsync(_context.RawStream, cancellationToken).ConfigureAwait(false);

                            if (indirectBlock != 0)
                            {
                                _context.RawStream.Position = indirectBlock * (long)blockSize +
                                                              logicalBlock % (blockSize / 4) * 4;
                                physicalBlock = await EndianUtilities.ReadUInt32LittleEndianAsync(_context.RawStream, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        throw new NotSupportedException("Triple indirection");
                    }
                }
            }

            var toRead = (int)Math.Min(totalBytesRemaining, blockSize - blockOffset);
            int numRead;
            if (physicalBlock == 0)
            {
                buffer.Span.Slice(totalRead, toRead).Clear();
                numRead = toRead;
            }
            else
            {
                _context.RawStream.Position = physicalBlock * (long)blockSize + blockOffset;
                numRead = await _context.RawStream.ReadAsync(buffer.Slice(totalRead, toRead), cancellationToken).ConfigureAwait(false);
            }

            totalBytesRemaining -= numRead;
            totalRead += numRead;
        }

        return totalRead;
    }

    public override int Read(long pos, Span<byte> buffer)
    {
        if (pos > _inode.FileSize)
        {
            return 0;
        }

        var blockSize = _context.SuperBlock.BlockSize;

        var totalRead = 0;
        var totalBytesRemaining = (int)Math.Min(buffer.Length, _inode.FileSize - pos);

        Span<byte> indirectData = stackalloc byte[4];

        while (totalBytesRemaining > 0)
        {
            var logicalBlock = (uint)((pos + totalRead) / blockSize);
            var blockOffset = (int)(pos + totalRead - logicalBlock * (long)blockSize);

            uint physicalBlock = 0;
            if (logicalBlock < 12)
            {
                physicalBlock = _inode.DirectBlocks[logicalBlock];
            }
            else
            {
                logicalBlock -= 12;
                if (logicalBlock < blockSize / 4)
                {
                    if (_inode.IndirectBlock != 0)
                    {
                        _context.RawStream.Position = _inode.IndirectBlock * (long)blockSize + logicalBlock * 4;
                        _context.RawStream.ReadExactly(indirectData);
                        physicalBlock = EndianUtilities.ToUInt32LittleEndian(indirectData);
                    }
                }
                else
                {
                    logicalBlock -= blockSize / 4;
                    if (logicalBlock < blockSize / 4 * (blockSize / 4))
                    {
                        if (_inode.DoubleIndirectBlock != 0)
                        {
                            _context.RawStream.Position = _inode.DoubleIndirectBlock * (long)blockSize +
                                                          logicalBlock / (blockSize / 4) * 4;
                            _context.RawStream.ReadExactly(indirectData);
                            var indirectBlock = EndianUtilities.ToUInt32LittleEndian(indirectData);

                            if (indirectBlock != 0)
                            {
                                _context.RawStream.Position = indirectBlock * (long)blockSize +
                                                              logicalBlock % (blockSize / 4) * 4;
                                _context.RawStream.ReadExactly(indirectData);
                                physicalBlock = EndianUtilities.ToUInt32LittleEndian(indirectData);
                            }
                        }
                    }
                    else
                    {
                        throw new NotSupportedException("Triple indirection");
                    }
                }
            }

            var toRead = (int)Math.Min(totalBytesRemaining, blockSize - blockOffset);
            int numRead;
            if (physicalBlock == 0)
            {
                buffer.Slice(totalRead, toRead).Clear();
                numRead = toRead;
            }
            else
            {
                _context.RawStream.Position = physicalBlock * (long)blockSize + blockOffset;
                numRead = _context.RawStream.Read(buffer.Slice(totalRead, toRead));
            }

            totalBytesRemaining -= numRead;
            totalRead += numRead;
        }

        return totalRead;
    }

    public override void Write(long pos, byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    public override void Write(long pos, ReadOnlySpan<byte> buffer) =>
        throw new NotImplementedException();

    public override void SetCapacity(long value)
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<Streams.StreamExtent> GetExtentsInRange(long start, long count)
    {
        return StreamExtent.Intersect(
            SingleValueEnumerable.Get(new Streams.StreamExtent(0, Capacity)),
            new Streams.StreamExtent(start, count));
    }
}