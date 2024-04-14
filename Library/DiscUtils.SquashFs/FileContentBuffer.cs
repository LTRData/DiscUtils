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
using System.Runtime.InteropServices;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;

namespace DiscUtils.SquashFs;

internal class FileContentBuffer : Streams.Buffer
{
    private const uint InvalidFragmentKey = 0xFFFFFFFF;

    private readonly int[] _blockLengths;
    private readonly Context _context;
    private readonly RegularInode _inode;

    public FileContentBuffer(Context context, RegularInode inode, MetadataRef inodeRef)
    {
        _context = context;
        _inode = inode;

        context.InodeReader.SetPosition(inodeRef);
        context.InodeReader.Skip(_inode.Size);

        var numBlocks = (int)(_inode.FileSize / _context.SuperBlock.BlockSize);
        if (_inode.FileSize % _context.SuperBlock.BlockSize != 0 && _inode.FragmentKey == InvalidFragmentKey)
        {
            ++numBlocks;
        }

        _blockLengths = new int[numBlocks];

        var lengthData = MemoryMarshal.AsBytes(_blockLengths.AsSpan());
        context.InodeReader.Read(lengthData);

        if (!BitConverter.IsLittleEndian)
        {
            for (var i = 0; i < numBlocks; ++i)
            {
                _blockLengths[i] = EndianUtilities.ToInt32LittleEndian(lengthData.Slice(i * 4, 4));
            }
        }
    }

    public override bool CanRead => true;

    public override bool CanWrite => false;

    public override long Capacity => _inode.FileSize;

    public override IEnumerable<StreamExtent> Extents
        => SingleValueEnumerable.Get(new StreamExtent(0, Capacity));

    public override int Read(long pos, byte[] buffer, int offset, int count)
    {
        if (pos > _inode.FileSize)
        {
            return 0;
        }

        var startOfFragment = _blockLengths.Length * _context.SuperBlock.BlockSize;
        var currentPos = pos;
        var totalRead = 0;
        var totalToRead = (int)Math.Min(_inode.FileSize - pos, count);
        var currentBlock = 0;
        long currentBlockDiskStart = _inode.StartBlock;
        while (totalRead < totalToRead)
        {
            if (currentPos >= startOfFragment)
            {
                var read = ReadFrag((int)(currentPos - startOfFragment), buffer, offset + totalRead,
                    totalToRead - totalRead);
                return totalRead + read;
            }

            var targetBlock = (int)(currentPos / _context.SuperBlock.BlockSize);
            while (currentBlock < targetBlock)
            {
                currentBlockDiskStart += _blockLengths[currentBlock] & 0x7FFFFF;
                ++currentBlock;
            }

            var blockOffset = (int)(pos % _context.SuperBlock.BlockSize);

            var block = _context.ReadBlock(currentBlockDiskStart, _blockLengths[currentBlock]);

            var toCopy = Math.Min(block.Available - blockOffset, totalToRead - totalRead);
            System.Buffer.BlockCopy(block.Data, blockOffset, buffer, offset + totalRead, toCopy);
            totalRead += toCopy;
            currentPos += toCopy;
        }

        return totalRead;
    }

    public override void Write(long pos, byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override int Read(long pos, Span<byte> buffer)
    {
        if (pos > _inode.FileSize)
        {
            return 0;
        }

        var startOfFragment = _blockLengths.Length * _context.SuperBlock.BlockSize;
        var currentPos = pos;
        var totalRead = 0;
        var totalToRead = (int)Math.Min(_inode.FileSize - pos, buffer.Length);
        var currentBlock = 0;
        long currentBlockDiskStart = _inode.StartBlock;
        while (totalRead < totalToRead)
        {
            if (currentPos >= startOfFragment)
            {
                var read = ReadFrag((int)(currentPos - startOfFragment), buffer.Slice(totalRead, totalToRead - totalRead));
                return totalRead + read;
            }

            var targetBlock = (int)(currentPos / _context.SuperBlock.BlockSize);
            while (currentBlock < targetBlock)
            {
                currentBlockDiskStart += _blockLengths[currentBlock] & 0x7FFFFF;
                ++currentBlock;
            }

            var blockOffset = (int)(pos % _context.SuperBlock.BlockSize);

            var block = _context.ReadBlock(currentBlockDiskStart, _blockLengths[currentBlock]);

            var toCopy = Math.Min(block.Available - blockOffset, totalToRead - totalRead);
            block.Data.AsSpan(blockOffset, toCopy).CopyTo(buffer.Slice(totalRead));
            totalRead += toCopy;
            currentPos += toCopy;
        }

        return totalRead;
    }

    public override void Write(long pos, ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException();

    public override void Clear(long pos, int count)
    {
        throw new NotSupportedException();
    }

    public override void Flush() {}

    public override void SetCapacity(long value)
    {
        throw new NotSupportedException();
    }

    public override IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
    {
        return StreamExtent.Intersect(Extents, new StreamExtent(start, count));
    }

    private int ReadFrag(int pos, byte[] buffer, int offset, int count)
    {
        var fragRecordsPerBlock = 8192 / FragmentRecord.RecordSize;
        var fragTable = (int)_inode.FragmentKey / fragRecordsPerBlock;
        var recordOffset = (int)(_inode.FragmentKey % fragRecordsPerBlock) * FragmentRecord.RecordSize;

        var fragRecordData = new byte[FragmentRecord.RecordSize];

        _context.FragmentTableReaders[fragTable].SetPosition(0, recordOffset);
        _context.FragmentTableReaders[fragTable].Read(fragRecordData, 0, fragRecordData.Length);

        var fragRecord = new FragmentRecord();
        fragRecord.ReadFrom(fragRecordData);

        var frag = _context.ReadBlock(fragRecord.StartBlock, fragRecord.CompressedSize);

        // Attempt to read data beyond end of fragment
        if (pos > frag.Available)
        {
            return 0;
        }

        var toCopy = (int)Math.Min(frag.Available - (_inode.FragmentOffset + pos), count);
        System.Buffer.BlockCopy(frag.Data, (int)(_inode.FragmentOffset + pos), buffer, offset, toCopy);
        return toCopy;
    }

    private int ReadFrag(int pos, Span<byte> buffer)
    {
        var fragRecordsPerBlock = 8192 / FragmentRecord.RecordSize;
        var fragTable = (int)_inode.FragmentKey / fragRecordsPerBlock;
        var recordOffset = (int)(_inode.FragmentKey % fragRecordsPerBlock) * FragmentRecord.RecordSize;

        var fragRecordData = new byte[FragmentRecord.RecordSize];

        _context.FragmentTableReaders[fragTable].SetPosition(0, recordOffset);
        _context.FragmentTableReaders[fragTable].Read(fragRecordData, 0, fragRecordData.Length);

        var fragRecord = new FragmentRecord();
        fragRecord.ReadFrom(fragRecordData);

        var frag = _context.ReadBlock(fragRecord.StartBlock, fragRecord.CompressedSize);

        // Attempt to read data beyond end of fragment
        if (pos > frag.Available)
        {
            return 0;
        }

        var toCopy = (int)Math.Min(frag.Available - (_inode.FragmentOffset + pos), buffer.Length);
        frag.Data.AsSpan((int)(_inode.FragmentOffset + pos), toCopy).CopyTo(buffer);
        return toCopy;
    }
}