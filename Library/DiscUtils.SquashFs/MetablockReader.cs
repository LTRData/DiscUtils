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
using DiscUtils.Streams;

namespace DiscUtils.SquashFs;

internal sealed class MetablockReader
{
    private readonly Context _context;

    private long _currentBlockStart;
    private int _currentOffset;
    private readonly long _start;

    public MetablockReader(Context context, long start)
    {
        _context = context;
        _start = start;
    }

    public void SetPosition(MetadataRef position)
    {
        SetPosition(position.Block, position.Offset);
    }

    public void SetPosition(long blockStart, int blockOffset)
    {
        if (blockOffset is < 0 or >= VfsSquashFileSystemReader.MetadataBufferSize)
        {
            throw new ArgumentOutOfRangeException(nameof(blockOffset), blockOffset,
                "Offset must be positive and less than block size");
        }

        _currentBlockStart = blockStart;
        _currentOffset = blockOffset;
    }

    /// <summary>
    /// Calculates the distance between the current position and the specified block position and offset.
    /// </summary>
    /// <remarks>
    /// * BlockStart is always related to the start of the metablock in the raw data stream
    /// * BlockOffset is the offset within the uncompressed data stream!
    ///
    /// This means, that there can never be a distance calculation based on start and offset
    /// across block boundaries
    ///
    /// Therefor to calculate the distance across block boundaries we need to iteratively read
    /// through all blocks from the starting block to the current block.
    ///
    /// In most (standard) cases everything will be within the same block and the calculation is trivial.
    /// </remarks>
    /// <param name="blockStart">The start of the metadatablock with the raw data</param>
    /// <param name="blockOffset">The offset within the uncompressed block</param>
    /// <returns>The distance between </returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public long DistanceFrom(long blockStart, int blockOffset)
    {
        if (blockStart > _currentBlockStart)
        {
            throw new ArgumentOutOfRangeException("Block start needs to be less than or equal to current block start");
        }

        if (blockStart == _currentBlockStart)
        {
            return _currentOffset - blockOffset;
        }

        var block = _context.ReadMetaBlock(_start + blockStart);
        long distance = Metablock.SQUASHFS_METADATA_SIZE - blockOffset;

        do
        {
            block = _context.ReadMetaBlock(block.NextBlockStart);
            blockStart = block.Position - _start;

            if (blockStart == _currentBlockStart)
            {
                distance += _currentOffset;
            }
            else
            {
                distance += block.Data.LongLength;
            }
        } while (blockStart != _currentBlockStart);

        return distance;
    }

    public void Skip(int count)
    {
        var block = _context.ReadMetaBlock(_start + _currentBlockStart);

        var totalSkipped = 0;
        while (totalSkipped < count)
        {
            if (_currentOffset >= block.Available)
            {
                var oldAvailable = block.Available;
                block = _context.ReadMetaBlock(block.NextBlockStart);
                _currentBlockStart = block.Position - _start;
                _currentOffset -= oldAvailable;
            }

            var toSkip = Math.Min(count - totalSkipped, block.Available - _currentOffset);
            totalSkipped += toSkip;
            _currentOffset += toSkip;
        }
    }

    public int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public int Read(Span<byte> buffer)
    {
        var block = _context.ReadMetaBlock(_start + _currentBlockStart);

        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            if (_currentOffset >= block.Available)
            {
                var oldAvailable = block.Available;
                block = _context.ReadMetaBlock(block.NextBlockStart);
                _currentBlockStart = block.Position - _start;
                _currentOffset -= oldAvailable;
            }

            var toRead = Math.Min(buffer.Length - totalRead, block.Available - _currentOffset);
            block.Data.AsSpan(_currentOffset, toRead).CopyTo(buffer.Slice(totalRead));
            totalRead += toRead;
            _currentOffset += toRead;
        }

        return totalRead;
    }

    public uint ReadUInt()
    {
        var block = _context.ReadMetaBlock(_start + _currentBlockStart);

        if (block.Available - _currentOffset < 4)
        {
            Span<byte> buffer = stackalloc byte[4];
            buffer = buffer.Slice(0, Read(buffer));
            return EndianUtilities.ToUInt32LittleEndian(buffer);
        }

        var result = EndianUtilities.ToUInt32LittleEndian(block.Data, _currentOffset);
        _currentOffset += 4;
        return result;
    }

    public int ReadInt()
    {
        var block = _context.ReadMetaBlock(_start + _currentBlockStart);

        if (block.Available - _currentOffset < 4)
        {
            Span<byte> buffer = stackalloc byte[4];
            buffer = buffer.Slice(0, Read(buffer));
            return EndianUtilities.ToInt32LittleEndian(buffer);
        }

        var result = EndianUtilities.ToInt32LittleEndian(block.Data, _currentOffset);
        _currentOffset += 4;
        return result;
    }

    public ushort ReadUShort()
    {
        var block = _context.ReadMetaBlock(_start + _currentBlockStart);

        if (block.Available - _currentOffset < 2)
        {
            Span<byte> buffer = stackalloc byte[2];
            buffer = buffer.Slice(0, Read(buffer));
            return EndianUtilities.ToUInt16LittleEndian(buffer);
        }

        var result = EndianUtilities.ToUInt16LittleEndian(block.Data, _currentOffset);
        _currentOffset += 2;
        return result;
    }

    public short ReadShort()
    {
        var block = _context.ReadMetaBlock(_start + _currentBlockStart);

        if (block.Available - _currentOffset < 2)
        {
            Span<byte> buffer = stackalloc byte[2];
            buffer = buffer.Slice(0, Read(buffer));
            return EndianUtilities.ToInt16LittleEndian(buffer);
        }

        var result = EndianUtilities.ToInt16LittleEndian(block.Data, _currentOffset);
        _currentOffset += 2;
        return result;
    }

    public string ReadString(int len)
    {
        var block = _context.ReadMetaBlock(_start + _currentBlockStart);
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        if (block.Available - _currentOffset < len)
        {
            Span<byte> buffer = stackalloc byte[len];
            buffer = buffer.Slice(0, Read(buffer));
            return latin1Encoding.GetString(buffer);
        }

        var result = latin1Encoding.GetString(block.Data, _currentOffset, len);

        _currentOffset += len;
        return result;
    }
}