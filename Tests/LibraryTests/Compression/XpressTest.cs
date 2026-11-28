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

    [WindowsOnlyTheory]
    [MemberData(nameof(RoundTripSamples), MemberType = typeof(NativeCompression))]
    public void DecompressMatchesWindowsCompressor(string kind, int length)
    {
        var original = Sample(kind, length);
        foreach (var engine in new ushort[] { 0, EngineMaximum })
        {
            if (TryNativeCompress(original, 0, original.Length, 4096, CompressionFormat.Xpress, engine) is not { } compressed)
            {
                continue;
            }

            var decompressed = new byte[original.Length];
            Assert.True(XpressLz77.TryDecompress(compressed, decompressed, out var written), $"engine 0x{engine:X}: decoding failed");
            Assert.Equal(original.Length, written);
            Assert.Equal(original, decompressed);
        }
    }

    [Fact]
    public void DecodesLiteralsMatchesAndTheSharedLengthHalfByte()
    {
        // Flags 0b0001_1111...: "abc", a match {offset 3, length 7 -> half-byte 15 -> byte 5: 30}, a match {offset 3, length from the
        // other half-byte: 10}, then the end-of-stream marker (a match flag at the end of the input).
        byte[] input = [0xFF, 0xFF, 0xFF, 0x1F, (byte)'a', (byte)'b', (byte)'c', 0x17, 0x00, 0x0F, 0x05, 0x17, 0x00];
        var output = new byte[43];
        Assert.True(XpressLz77.TryDecompress(input, output, out var written));
        Assert.Equal(43, written);
        Assert.Equal(string.Concat(System.Linq.Enumerable.Repeat("abc", 15)).Substring(0, 43), System.Text.Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void RejectsAMatchBeforeTheStartOfTheOutput()
    {
        byte[] input = [0x00, 0x00, 0x00, 0x80, 0x10, 0x00];
        Assert.False(XpressLz77.TryDecompress(input, new byte[8], out _));
    }
}
