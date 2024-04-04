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

namespace DiscUtils.Ntfs;

internal struct IndexRoot : IByteArraySerializable, IDiagnosticTraceable
{
    public const int HeaderOffset = 0x10;

    public uint AttributeType { get; set; }

    public AttributeCollationRule CollationRule { get; set; }

    public uint IndexAllocationSize { get; set; }

    public byte RawClustersPerIndexRecord { get; set; }

    public int Size
    {
        get { return 16; }
    }

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        AttributeType = EndianUtilities.ToUInt32LittleEndian(buffer);
        CollationRule = (AttributeCollationRule)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x04));
        IndexAllocationSize = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x08));
        RawClustersPerIndexRecord = buffer[0x0C];
        return 16;
    }

    public void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian(AttributeType, buffer);
        EndianUtilities.WriteBytesLittleEndian((uint)CollationRule, buffer.Slice(0x04));
        EndianUtilities.WriteBytesLittleEndian(IndexAllocationSize, buffer.Slice(0x08));
        EndianUtilities.WriteBytesLittleEndian(RawClustersPerIndexRecord, buffer.Slice(0x0C));
    }

    public void Dump(TextWriter writer, string indent)
    {
        writer.WriteLine($"{indent}                Attr Type: {AttributeType}");
        writer.WriteLine($"{indent}           Collation Rule: {CollationRule}");
        writer.WriteLine($"{indent}         Index Alloc Size: {IndexAllocationSize}");
        writer.WriteLine($"{indent}  Raw Clusters Per Record: {RawClustersPerIndexRecord}");
    }

    public IComparer<byte[]> GetCollator(UpperCase upCase)
    {
        return CollationRule switch
        {
            AttributeCollationRule.Filename => new FileNameComparer(upCase),
            AttributeCollationRule.SecurityHash => new SecurityHashComparer(),
            AttributeCollationRule.UnsignedLong => new UnsignedLongComparer(),
            AttributeCollationRule.MultipleUnsignedLongs => new MultipleUnsignedLongComparer(),
            AttributeCollationRule.Sid => new SidComparer(),
            _ => throw new NotImplementedException(),
        };
    }

    private struct SecurityHashComparer : IComparer<byte[]>
    {
        public int Compare(byte[] x, byte[] y)
        {
            if (x == null && y == null)
            {
                return 0;
            }
            if (y == null)
            {
                return -1;
            }
            if (x == null)
            {
                return 1;
            }

            var xHash = EndianUtilities.ToUInt32LittleEndian(x, 0);
            var yHash = EndianUtilities.ToUInt32LittleEndian(y, 0);

            if (xHash < yHash)
            {
                return -1;
            }
            if (xHash > yHash)
            {
                return 1;
            }

            var xId = EndianUtilities.ToUInt32LittleEndian(x, 4);
            var yId = EndianUtilities.ToUInt32LittleEndian(y, 4);
            if (xId < yId)
            {
                return -1;
            }
            if (xId > yId)
            {
                return 1;
            }
            return 0;
        }
    }

    private struct UnsignedLongComparer : IComparer<byte[]>
    {
        public int Compare(byte[] x, byte[] y)
        {
            if (x == null && y == null)
            {
                return 0;
            }
            if (y == null)
            {
                return -1;
            }
            if (x == null)
            {
                return 1;
            }

            var xVal = EndianUtilities.ToUInt32LittleEndian(x, 0);
            var yVal = EndianUtilities.ToUInt32LittleEndian(y, 0);

            if (xVal < yVal)
            {
                return -1;
            }
            if (xVal > yVal)
            {
                return 1;
            }

            return 0;
        }
    }

    private struct MultipleUnsignedLongComparer : IComparer<byte[]>
    {
        public int Compare(byte[] x, byte[] y)
        {
            if (x == null && y == null)
            {
                return 0;
            }

            if (y == null)
            {
                return -1;
            }

            if (x == null)
            {
                return 1;
            }

            for (var i = 0; i < x.Length / 4; ++i)
            {
                var xVal = EndianUtilities.ToUInt32LittleEndian(x, i * 4);
                var yVal = EndianUtilities.ToUInt32LittleEndian(y, i * 4);

                if (xVal < yVal)
                {
                    return -1;
                }
                if (xVal > yVal)
                {
                    return 1;
                }
            }

            return 0;
        }
    }

    private readonly struct FileNameComparer : IComparer<byte[]>
    {
        private readonly UpperCase _stringComparer;

        public FileNameComparer(UpperCase upCase)
        {
            _stringComparer = upCase;
        }

        public int Compare(byte[] x, byte[] y)
        {
            if (x == null && y == null)
            {
                return 0;
            }
            if (y == null)
            {
                return -1;
            }
            if (x == null)
            {
                return 1;
            }

            var xFnLen = x[0x40];
            var yFnLen = y[0x40];

            return _stringComparer.Compare(x, 0x42, xFnLen * 2, y, 0x42, yFnLen * 2);
        }
    }

    private struct SidComparer : IComparer<byte[]>
    {
        public int Compare(byte[] x, byte[] y)
        {
            if (x == null && y == null)
            {
                return 0;
            }
            if (y == null)
            {
                return -1;
            }
            if (x == null)
            {
                return 1;
            }

            var toComp = Math.Min(x.Length, y.Length);
            for (var i = 0; i < toComp; ++i)
            {
                var val = x[i] - y[i];
                if (val != 0)
                {
                    return val;
                }
            }

            if (x.Length < y.Length)
            {
                return -1;
            }
            if (x.Length > y.Length)
            {
                return 1;
            }

            return 0;
        }
    }
}