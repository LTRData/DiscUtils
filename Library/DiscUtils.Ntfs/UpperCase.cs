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
using System.IO.Compression;
using System.Runtime.InteropServices;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Ntfs;

internal readonly struct UpperCase : IComparer<string>
{
    private readonly char[] _table;

    public UpperCase(File file)
    {
        using var s = file.OpenStream(AttributeType.Data, null, FileAccess.Read);

        _table = new char[s.Length / 2];

        var bytes = MemoryMarshal.AsBytes(_table.AsSpan());

        s.ReadExactly(bytes);

        if (!BitConverter.IsLittleEndian)
        {
            for (var i = 0; i < _table.Length; ++i)
            {
                _table[i] = (char)EndianUtilities.ToUInt16LittleEndian(bytes[(i * 2)..]);
            }
        }
    }

    public UpperCase(char[] table)
    {
        _table = table;
    }

    public int Compare(string x, string y)
    {
        var compLen = Math.Min(x.Length, y.Length);
        for (var i = 0; i < compLen; ++i)
        {
            var result = _table[x[i]] - _table[y[i]];
            if (result != 0)
            {
                return result;
            }
        }

        // Identical out to the shortest string, so length is now the
        // determining factor.
        return x.Length - y.Length;
    }

    public int Compare(byte[] x, int xOffset, int xLength, byte[] y, int yOffset, int yLength)
    {
        var compLen = Math.Min(xLength, yLength) / 2;
        for (var i = 0; i < compLen; ++i)
        {
            var xCh = (char)(x[xOffset + i * 2] | (x[xOffset + i * 2 + 1] << 8));
            var yCh = (char)(y[yOffset + i * 2] | (y[yOffset + i * 2 + 1] << 8));

            var result = _table[xCh] - _table[yCh];
            if (result != 0)
            {
                return result;
            }
        }

        // Identical out to the shortest string, so length is now the
        // determining factor.
        return xLength - yLength;
    }

    #region UpperCaseExcludedBitmap
    private readonly static byte[] _upperCaseIncludedCompressed = [
        0xED, 0xD9, 0x3B, 0x0A, 0xC2, 0x40, 0x10, 0x06, 0xE0, 0xC1, 0x04, 0x4C, 0x62, 0x13, 0x0B, 0x6B,
        0x9F, 0x88, 0xE6, 0x04, 0x76, 0x49, 0x25, 0xB6, 0x4A, 0x72, 0x01, 0x2F, 0x12, 0x0B, 0x2B, 0x6D,
        0x3D, 0x81, 0x55, 0x9C, 0xDE, 0xA3, 0x58, 0x79, 0x09, 0x7B, 0x21, 0x23, 0xBB, 0x3E, 0x62, 0x14,
        0xC5, 0x4A, 0x45, 0xFF, 0xAF, 0xD8, 0xD9, 0xEC, 0xC0, 0xCE, 0xC2, 0xC2, 0x16, 0x13, 0xA2, 0x4C,
        0x2A, 0x52, 0xBC, 0xFA, 0x24, 0x91, 0x58, 0x58, 0x4B, 0xC2, 0x68, 0xA5, 0x27, 0x61, 0xB7, 0xDC,
        0xEA, 0x7B, 0xF5, 0xE1, 0x32, 0x18, 0x45, 0x6B, 0xE6, 0xCE, 0x31, 0xCB, 0x8E, 0x3B, 0xE5, 0xAD,
        0x63, 0xCF, 0xDB, 0x35, 0xBB, 0x6A, 0xD2, 0x63, 0xB3, 0x9E, 0x0E, 0xBB, 0x54, 0xF6, 0xF1, 0x84,
        0x99, 0x4D, 0xFB, 0x5C, 0x49, 0x51, 0x7B, 0x15, 0x92, 0x63, 0xC5, 0x70, 0xC3, 0x17, 0x4E, 0x76,
        0x3E, 0x89, 0x9F, 0xEC, 0x0E, 0xF0, 0xE3, 0x1A, 0xB7, 0x0B, 0x7C, 0xC7, 0xCB, 0xAD, 0x09, 0xF9,
        0x24, 0xA4, 0x46, 0x56, 0xD1, 0x57, 0x73, 0xA1, 0x12, 0x59, 0x64, 0x50, 0x93, 0xAC, 0xD7, 0x2B,
        0x07, 0x6A, 0x10, 0x71, 0xE9, 0x5D, 0x44, 0xC4, 0xA0, 0x3F, 0xA1, 0x9F, 0xBF, 0x78, 0x5C, 0x19,
        0xE4, 0xEE, 0x4E, 0xBD, 0x7B, 0x2A, 0xE1, 0x7F, 0xFA, 0x78, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x5F, 0x42, 0xF7, 0x21, 0x3D, 0x1D, 0xEF, 0x72, 0x09, 0x9F, 0x9A, 0x8A, 0x4C, 0x0B, 0x7E,
        0x5F, 0x03, 0x03, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3E, 0xE5, 0xF6, 0xDF, 0xFE, 0xD9, 0x01,
    ];
    #endregion

    private static char[] Table => field ??= CreateDefaultTable();

    private static char[] CreateDefaultTable()
    {
        using var decompressor = new DeflateStream(new MemoryStream(_upperCaseIncludedCompressed), CompressionMode.Decompress);
        
        var upperCaseIncluded = new byte[(char.MaxValue + 1) / 8];

        using var result = new MemoryStream(upperCaseIncluded);
        
        decompressor.CopyTo(result);

        var table = new char[char.MaxValue + 1];

        Span<byte> bytes = MemoryMarshal.AsBytes(table.AsSpan());

        for (int i = char.MinValue; i <= char.MaxValue; ++i)
        {
            var c = (char)i;

            if (upperCaseIncluded.GetBit(i))
            {
                c = char.ToUpperInvariant(c);
            }

            EndianUtilities.WriteBytesLittleEndian(c, bytes[(i * 2)..]);
        }

        return table;
    }

    internal static UpperCase Initialize(File file)
    {
        var table = (char[])Table.Clone();

        using (var s = file.OpenStream(AttributeType.Data, null, FileAccess.ReadWrite))
        {
            s.Write(MemoryMarshal.AsBytes(table.AsSpan()));
        }

        return new UpperCase(table);
    }
}
