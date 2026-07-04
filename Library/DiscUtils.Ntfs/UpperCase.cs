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
    private readonly static byte[] _upperCaseExcludedCompressed = [
        0xEC, 0xD9, 0x3D, 0x0E, 0x82, 0x30, 0x14, 0x07, 0xF0, 0x67, 0xE2, 0xEE, 0x25, 0x9C, 0x3C, 0x8A,
        0xFB, 0xB3, 0x93, 0x27, 0x70, 0xD5, 0xC9, 0x41, 0x2E, 0xE2, 0x05, 0x20, 0xBA, 0x1A, 0x27, 0xD9,
        0x3C, 0x01, 0x1A, 0xE3, 0xE7, 0xEC, 0x20, 0x8B, 0xA0, 0x09, 0x98, 0x27, 0x2D, 0xE2, 0x17, 0x31,
        0x71, 0x72, 0x90, 0xFF, 0x6F, 0xE8, 0xA3, 0x7D, 0x29, 0x05, 0x4A, 0x19, 0x8A, 0xC8, 0x43, 0x89,
        0xE8, 0xFC, 0x54, 0x15, 0x22, 0x8B, 0xD8, 0x50, 0x8E, 0xDD, 0x30, 0x07, 0xCE, 0xEC, 0xB0, 0x1C,
        0x7B, 0x9B, 0x61, 0x73, 0x32, 0xB0, 0xEB, 0xCC, 0xF3, 0x34, 0xCB, 0x81, 0xDF, 0xE5, 0x6A, 0x10,
        0xB6, 0x17, 0xDB, 0x70, 0x17, 0xC9, 0x67, 0x9D, 0xA9, 0x09, 0x95, 0x12, 0x95, 0xAD, 0x5E, 0xD2,
        0x33, 0x0A, 0xB3, 0x91, 0x34, 0x7D, 0xAE, 0x8B, 0x4A, 0x47, 0x74, 0x6A, 0x7C, 0x17, 0x3C, 0xAE,
        0x8F, 0x2C, 0x01, 0x28, 0xAC, 0xF5, 0x7B, 0x03, 0xE7, 0x78, 0x2F, 0x6D, 0x24, 0xAE, 0x90, 0xE8,
        0x92, 0x75, 0x74, 0xC9, 0xD4, 0x8E, 0x72, 0x92, 0x58, 0x56, 0x49, 0xF9, 0xB5, 0x49, 0xBA, 0x4E,
        0xFD, 0x9F, 0xDD, 0x6A, 0xB2, 0xD8, 0xE3, 0xA2, 0x4C, 0xAB, 0xF9, 0xFC, 0x59, 0xFD, 0xFD, 0xE8,
        0x65, 0xEE, 0x82, 0x5B, 0xC6, 0xC5, 0x7B, 0x0F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0xD9,
        0x3E, 0x88, 0xF2, 0x4C, 0xCC, 0xE5, 0x14, 0xDF, 0x36, 0x15, 0x59, 0x5A, 0xEC, 0xE3, 0x59, 0x01,
        0x00, 0x00, 0x00, 0xC0, 0xFF, 0x7B, 0xFF, 0xB7, 0x9F, 0xB9, 0x02, 0x00, 0x00, 0xFF, 0xFF,
    ];
    #endregion

    private static char[] Table => field ??= CreateDefaultTable();

    private static char[] CreateDefaultTable()
    {
        using var decompressor = new DeflateStream(new MemoryStream(_upperCaseExcludedCompressed), CompressionMode.Decompress);
        using var result = new MemoryStream();
        decompressor.CopyTo(result);

        var upperCaseExcluded = result.ToArray();

        var table = new char[char.MaxValue + 1];
        Span<byte> bytes = MemoryMarshal.AsBytes(table.AsSpan());

        for (int i = char.MinValue; i <= char.MaxValue; ++i)
        {
            var c = (char)i;

            if ((upperCaseExcluded[i >> 3] & (1 << (i & 7))) == 0)
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
