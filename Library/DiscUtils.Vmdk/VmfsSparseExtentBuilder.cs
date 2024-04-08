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

namespace DiscUtils.Vmdk;

internal sealed class VmfsSparseExtentBuilder : StreamBuilder
{
    private readonly SparseStream _content;

    public VmfsSparseExtentBuilder(SparseStream content)
    {
        _content = content;
    }

    protected override List<BuilderExtent> FixExtents(out long totalLength)
    {
        var extents = new List<BuilderExtent>();

        var header = DiskImageFile.CreateServerSparseExtentHeader(_content.Length);
        var gdExtent = new GlobalDirectoryExtent(header);

        var grainTableStart = header.GdOffset * Sizes.Sector + gdExtent.Length;
        var grainTableCoverage = header.NumGTEsPerGT * header.GrainSize * Sizes.Sector;

        foreach (var grainTableRange in StreamExtent.Blocks(_content.Extents, grainTableCoverage))
        {
            for (var i = 0; i < grainTableRange.Count; ++i)
            {
                var grainTable = grainTableRange.Offset + i;
                var dataStart = grainTable * grainTableCoverage;
                var gtExtent = new GrainTableExtent(grainTableStart,
                    new SubStream(_content, dataStart, Math.Min(grainTableCoverage, _content.Length - dataStart)),
                    header);
                extents.Add(gtExtent);
                gdExtent.SetEntry((int)grainTable, (uint)(grainTableStart / Sizes.Sector));

                grainTableStart += gtExtent.Length;
            }
        }

        extents.Insert(0, gdExtent);

        header.FreeSector = (uint)(grainTableStart / Sizes.Sector);

        var buffer = header.GetBytes();
        extents.Insert(0, new BuilderBufferExtent(0, buffer));

        totalLength = grainTableStart;

        return extents;
    }

    private class GlobalDirectoryExtent : BuilderExtent
    {
        private readonly byte[] _buffer;
        private MemoryStream _streamView;

        public GlobalDirectoryExtent(ServerSparseExtentHeader header)
            : base(header.GdOffset * Sizes.Sector, MathUtilities.RoundUp(header.NumGdEntries * 4, Sizes.Sector))
        {
            _buffer = new byte[Length];
        }

        protected override void Dispose(bool disposing)
        {
            if (_streamView != null)
            {
                if (disposing)
                {
                    _streamView.Dispose();
                }

                _streamView = null;
            }
        }

        public void SetEntry(int index, uint grainTableSector)
        {
            EndianUtilities.WriteBytesLittleEndian(grainTableSector, _buffer, index * 4);
        }

        public override void PrepareForRead()
        {
            _streamView = new MemoryStream(_buffer, 0, _buffer.Length, false);
        }

        public override int Read(long diskOffset, byte[] block, int offset, int count)
        {
            _streamView.Position = diskOffset - Start;
            return _streamView.Read(block, offset, count);
        }

        public override int Read(long diskOffset, Span<byte> block)
        {
            _streamView.Position = diskOffset - Start;
            return _streamView.Read(block);
        }

        public override void DisposeReadState()
        {
            if (_streamView != null)
            {
                _streamView.Dispose();
                _streamView = null;
            }
        }
    }

    private class GrainTableExtent : BuilderExtent
    {
        private SparseStream _content;
        private readonly Ownership _contentOwnership;
        private List<long> _grainContiguousRangeMapping;
        private List<long> _grainMapping;

        private MemoryStream _grainTableStream;
        private readonly ServerSparseExtentHeader _header;

        public GrainTableExtent(long outputStart, SparseStream content, ServerSparseExtentHeader header)
            : this(outputStart, content, Ownership.None, header) {}

        public GrainTableExtent(long outputStart, SparseStream content, Ownership contentOwnership,
                                ServerSparseExtentHeader header)
            : base(outputStart, CalcSize(content, header))
        {
            _content = content;
            _contentOwnership = contentOwnership;
            _header = header;
        }

        protected override void Dispose(bool disposing)
        {
            if (_content != null && _contentOwnership == Ownership.Dispose)
            {
                if (disposing)
                {
                    _content.Dispose();
                }

                _content = null;
            }

            if (_grainTableStream != null)
            {
                _grainTableStream.Dispose();
                _grainTableStream = null;
            }
        }

        public override void PrepareForRead()
        {
            var grainTable = new byte[MathUtilities.RoundUp(_header.NumGTEsPerGT * 4, Sizes.Sector)];

            var dataSector = (Start + grainTable.Length) / Sizes.Sector;

            _grainMapping = new List<long>();
            _grainContiguousRangeMapping = new List<long>();
            foreach (var grainRange in StreamExtent.Blocks(_content.Extents, _header.GrainSize * Sizes.Sector))
            {
                for (var i = 0; i < grainRange.Count; ++i)
                {
                    EndianUtilities.WriteBytesLittleEndian((uint)dataSector, grainTable,
                        (int)(4 * (grainRange.Offset + i)));
                    dataSector += _header.GrainSize;
                    _grainMapping.Add(grainRange.Offset + i);
                    _grainContiguousRangeMapping.Add(grainRange.Count - i);
                }
            }

            _grainTableStream = new MemoryStream(grainTable, 0, grainTable.Length, false);
        }

        public override int Read(long diskOffset, byte[] block, int offset, int count)
        {
            var relOffset = diskOffset - Start;

            if (relOffset < _grainTableStream.Length)
            {
                _grainTableStream.Position = relOffset;
                return _grainTableStream.Read(block, offset, count);
            }

            var grainSize = _header.GrainSize * Sizes.Sector;
            var grainIdx = (int)((relOffset - _grainTableStream.Length) / grainSize);
            var grainOffset = relOffset - _grainTableStream.Length - grainIdx * grainSize;

            var maxToRead =
                (int)Math.Min(count, grainSize * _grainContiguousRangeMapping[grainIdx] - grainOffset);

            _content.Position = _grainMapping[grainIdx] * grainSize + grainOffset;
            return _content.Read(block, offset, maxToRead);
        }

        public override int Read(long diskOffset, Span<byte> block)
        {
            var relOffset = diskOffset - Start;

            if (relOffset < _grainTableStream.Length)
            {
                _grainTableStream.Position = relOffset;
                return _grainTableStream.Read(block);
            }

            var grainSize = _header.GrainSize * Sizes.Sector;
            var grainIdx = (int)((relOffset - _grainTableStream.Length) / grainSize);
            var grainOffset = relOffset - _grainTableStream.Length - grainIdx * grainSize;

            var maxToRead =
                (int)Math.Min(block.Length, grainSize * _grainContiguousRangeMapping[grainIdx] - grainOffset);

            _content.Position = _grainMapping[grainIdx] * grainSize + grainOffset;
            return _content.Read(block.Slice(0, maxToRead));
        }

        public override void DisposeReadState()
        {
            if (_grainTableStream != null)
            {
                _grainTableStream.Dispose();
                _grainTableStream = null;
            }

            _grainMapping = null;
            _grainContiguousRangeMapping = null;
        }

        private static long CalcSize(SparseStream content, ServerSparseExtentHeader header)
        {
            var numDataGrains = StreamExtent.BlockCount(content.Extents, header.GrainSize * Sizes.Sector);
            long grainTableSectors = MathUtilities.Ceil(header.NumGTEsPerGT * 4, Sizes.Sector);

            return (grainTableSectors + numDataGrains * header.GrainSize) * Sizes.Sector;
        }
    }
}