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
using DiscUtils.Compression;
using static LibraryTests.Compression.NativeCompression;

namespace LibraryTests.Compression;

public class XpressTest
{
    private byte[] _uncompressedData;

    public XpressTest()
    {
        var rng = new Random(3425);
        _uncompressedData = new byte[64 * 1024];

        // Some test data that is reproducible, and fairly compressible
        for (var i = 0; i < 16 * 4096; ++i)
        {
            var b = (byte)(rng.Next(26) + 'A');
            var start = rng.Next(_uncompressedData.Length);
            var len = rng.Next(20);

            for (var j = start; j < _uncompressedData.Length && j < start + len; j++)
            {
                _uncompressedData[j] = b;
            }
        }

        // Make one block uncompressible
        for (var i = 5 * 4096; i < 6 * 4096; ++i)
        {
            _uncompressedData[i] = (byte)rng.Next(256);
        }
    }

    [WindowsOnlyFact]
    public void Decompress()
    {
        var compressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096, CompressionFormat.Xpress);

        // Double-check, make sure native code round-trips
        Assert.Equal(_uncompressedData, NativeDecompress(compressed, 0, compressed.Length, CompressionFormat.Xpress));

        var decompressed = new byte[_uncompressedData.Length];
        var rc = XpressLz77.TryDecompress(compressed, decompressed, out var numDecompressed);
        Assert.True(rc);
        Assert.Equal(numDecompressed, _uncompressedData.Length);

        Assert.Equal(_uncompressedData, decompressed);
    }

    [WindowsOnlyFact]
    public void DecompressMidSourceBuffer()
    {
        var compressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096, CompressionFormat.Xpress);

        var inData = new byte[128 * 1024];
        Buffer.BlockCopy(compressed, 0, inData, 32 * 1024, compressed.Length);

        // Double-check, make sure native code round-trips
        Assert.Equal(_uncompressedData, NativeDecompress(inData, 32 * 1024, compressed.Length, CompressionFormat.Xpress));

        var decompressed = new byte[_uncompressedData.Length];
        var rc = XpressLz77.TryDecompress(inData.AsSpan(32 * 1024, compressed.Length), decompressed, out var numDecompressed);
        Assert.True(rc);
        Assert.Equal(numDecompressed, _uncompressedData.Length);

        Assert.Equal(_uncompressedData, decompressed);
    }

    [WindowsOnlyFact]
    public void Decompress1KBlockSize()
    {
        var compressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 1024, CompressionFormat.Xpress);

        Assert.Equal(_uncompressedData, NativeDecompress(compressed, 0, compressed.Length, CompressionFormat.Xpress));

        var decompressed = new byte[_uncompressedData.Length];
        var rc = XpressLz77.TryDecompress(compressed, decompressed, out var numDecompressed);
        Assert.True(rc);
        Assert.Equal(numDecompressed, _uncompressedData.Length);

        Assert.Equal(_uncompressedData, decompressed);
    }
}
