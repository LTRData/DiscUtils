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
using System.Buffers;
using System.Collections.Generic;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.SquashFs;

internal sealed class FragmentWriter
{
    private readonly BuilderContext _context;

    private readonly byte[] _currentBlock;
    private int _currentOffset;

    private readonly List<FragmentRecord> _fragmentBlocks;

    public FragmentWriter(BuilderContext context)
    {
        _context = context;
        _currentBlock = new byte[context.DataBlockSize];
        _currentOffset = 0;

        _fragmentBlocks = [];
    }

    public int FragmentCount { get; private set; }

    public int FragmentBlocksCount => _fragmentBlocks.Count;

    public void Flush()
    {
        if (_currentOffset != 0)
        {
            NextBlock();
        }
    }

    public uint WriteFragment(int length, out uint offset)
    {
        if (_currentBlock.Length - _currentOffset < length)
        {
            NextBlock();
            _currentOffset = 0;
        }

        offset = (uint)_currentOffset;
        System.Buffer.BlockCopy(_context.IoBuffer, 0, _currentBlock, _currentOffset, length);
        _currentOffset += length;

        ++FragmentCount;

        return (uint)_fragmentBlocks.Count;
    }

    internal long Persist()
    {
        if (_fragmentBlocks.Count <= 0)
        {
            return -1;
        }

        if (_fragmentBlocks.Count * FragmentRecord.RecordSize > _context.DataBlockSize)
        {
            throw new NotImplementedException("Large numbers of fragments");
        }

        // Persist the table that references the block containing the fragment records
        var blockPos = _context.RawStream.Position;
        var recordSize = FragmentRecord.RecordSize;
        var buffer = ArrayPool<byte>.Shared.Rent(_fragmentBlocks.Count * recordSize);
        try
        {
            for (var i = 0; i < _fragmentBlocks.Count; ++i)
            {
                _fragmentBlocks[i].WriteTo(buffer, i * recordSize);
            }

            var writer = new MetablockWriter();
            writer.Write(buffer, 0, _fragmentBlocks.Count * recordSize);
            writer.Persist(_context.RawStream);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var tablePos = _context.RawStream.Position;
        Span<byte> tableBuffer = stackalloc byte[8];
        EndianUtilities.WriteBytesLittleEndian(blockPos, tableBuffer);
        _context.RawStream.Write(tableBuffer);

        return tablePos;
    }

    private void NextBlock()
    {
        var position = _context.RawStream.Position;

        var writeLen = _context.WriteDataBlock(_currentBlock, 0, _currentOffset);
        var blockRecord = new FragmentRecord
        {
            StartBlock = position,
            CompressedSize = (int)writeLen
        };

        _fragmentBlocks.Add(blockRecord);
    }
}