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
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Vhd;

internal sealed class DynamicDiskBuilder : StreamBuilder
{
    private readonly uint _blockSize;
    private readonly SparseStream _content;
    private readonly Footer _footer;

    public DynamicDiskBuilder(SparseStream content, Footer footer, uint blockSize)
    {
        _content = content;
        _footer = footer;
        _blockSize = blockSize;
    }

    protected override List<BuilderExtent> FixExtents(out long totalLength)
    {
        const int FooterSize = 512;
        const int DynHeaderSize = 1024;

        var extents = new List<BuilderExtent>();

        _footer.DataOffset = FooterSize;

        var dynHeader = new DynamicHeader(-1, FooterSize + DynHeaderSize, _blockSize, _footer.CurrentSize);

        var batExtent = new BlockAllocationTableExtent(FooterSize + DynHeaderSize,
            dynHeader.MaxTableEntries);

        var streamPos = batExtent.Start + batExtent.Length;

        foreach (var blockRange in StreamExtent.Blocks(_content.Extents, _blockSize))
        {
            for (var i = 0; i < blockRange.Count; ++i)
            {
                var block = blockRange.Offset + i;
                var blockStart = block * _blockSize;
                var dataExtent = new DataBlockExtent(streamPos,
                    new SubStream(_content, blockStart, Math.Min(_blockSize, _content.Length - blockStart)));
                extents.Add(dataExtent);

                batExtent.SetEntry((int)block, (uint)(streamPos / Sizes.Sector));

                streamPos += dataExtent.Length;
            }
        }

        _footer.UpdateChecksum();
        dynHeader.UpdateChecksum();

        var footerBuffer = new byte[FooterSize];
        _footer.ToBytes(footerBuffer);

        var dynHeaderBuffer = new byte[DynHeaderSize];
        dynHeader.ToBytes(dynHeaderBuffer);

        // Add footer (to end)
        extents.Add(new BuilderBufferExtent(streamPos, footerBuffer));
        totalLength = streamPos + FooterSize;

        extents.Insert(0, batExtent);
        extents.Insert(0, new BuilderBufferExtent(FooterSize, dynHeaderBuffer));
        extents.Insert(0, new BuilderBufferExtent(0, footerBuffer));

        return extents;
    }

    private class BlockAllocationTableExtent : BuilderExtent
    {
        private MemoryStream _dataStream;
        private readonly uint[] _entries;

        public BlockAllocationTableExtent(long start, int maxEntries)
            : base(start, MathUtilities.RoundUp(maxEntries * 4, 512))
        {
            _entries = new uint[Length / 4];
            for (var i = 0; i < _entries.Length; ++i)
            {
                _entries[i] = 0xFFFFFFFF;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_dataStream != null)
            {
                if (disposing)
                {
                    _dataStream.Dispose();
                }

                _dataStream = null;
            }
        }

        public void SetEntry(int index, uint fileSector)
        {
            _entries[index] = fileSector;
        }

        public override void PrepareForRead()
        {
            var buffer = new byte[Length];

            for (var i = 0; i < _entries.Length; ++i)
            {
                EndianUtilities.WriteBytesBigEndian(_entries[i], buffer, i * 4);
            }

            _dataStream = new MemoryStream(buffer, false);
        }

        public override int Read(long diskOffset, byte[] block, int offset, int count)
        {
            _dataStream.Position = diskOffset - Start;
            return _dataStream.Read(block, offset, count);
        }

        public override int Read(long diskOffset, Span<byte> block)
        {
            _dataStream.Position = diskOffset - Start;
            return _dataStream.Read(block);
        }

        public override void DisposeReadState()
        {
            if (_dataStream != null)
            {
                _dataStream.Dispose();
                _dataStream = null;
            }
        }
    }

    private class DataBlockExtent : BuilderExtent
    {
        private MemoryStream _bitmapStream;
        private SparseStream _content;
        private readonly Ownership _ownership;

        public DataBlockExtent(long start, SparseStream content)
            : this(start, content, Ownership.None) {}

        public DataBlockExtent(long start, SparseStream content, Ownership ownership)
            : base(
                start,
                MathUtilities.RoundUp(MathUtilities.Ceil(content.Length, Sizes.Sector) / 8, Sizes.Sector) +
                MathUtilities.RoundUp(content.Length, Sizes.Sector))
        {
            _content = content;
            _ownership = ownership;
        }

        protected override void Dispose(bool disposing)
        {
            if (_content != null && _ownership == Ownership.Dispose)
            {
                if (disposing)
                {
                    _content.Dispose();
                }

                _content = null;
            }

            if (_bitmapStream != null)
            {
                if (disposing)
                {
                    _bitmapStream.Dispose();
                }

                _bitmapStream = null;
            }
        }

        public override void PrepareForRead()
        {
            var bitmap =
                new byte[MathUtilities.RoundUp(MathUtilities.Ceil(_content.Length, Sizes.Sector) / 8, Sizes.Sector)];

            foreach (var range in StreamExtent.Blocks(_content.Extents, Sizes.Sector))
            {
                for (var i = 0; i < range.Count; ++i)
                {
                    var mask = (byte)(1 << (7 - (int)(range.Offset + i) % 8));
                    bitmap[(range.Offset + i) / 8] |= mask;
                }
            }

            _bitmapStream = new MemoryStream(bitmap, false);
        }

        public override int Read(long diskOffset, byte[] block, int offset, int count)
        {
            var position = diskOffset - Start;
            if (position < _bitmapStream.Length)
            {
                _bitmapStream.Position = position;
                return _bitmapStream.Read(block, offset, count);
            }

            _content.Position = position - _bitmapStream.Length;
            return _content.Read(block, offset, count);
        }

        public override int Read(long diskOffset, Span<byte> block)
        {
            var position = diskOffset - Start;
            if (position < _bitmapStream.Length)
            {
                _bitmapStream.Position = position;
                return _bitmapStream.Read(block);
            }

            _content.Position = position - _bitmapStream.Length;
            return _content.Read(block);
        }

        public override void DisposeReadState()
        {
            if (_bitmapStream != null)
            {
                _bitmapStream.Dispose();
                _bitmapStream = null;
            }
        }
    }
}