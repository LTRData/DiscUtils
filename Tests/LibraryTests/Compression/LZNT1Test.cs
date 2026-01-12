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
using Xunit;
using static LibraryTests.Compression.NativeCompression;

namespace LibraryTests.Compression;

public class LZNT1Test
{
    private byte[] _uncompressedData;

    public LZNT1Test()
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
    public void Compress()
    {
        var instance = LZNT1.Default;
        var compressor = instance;

        var compressedLength = 16 * 4096;
        var compressedData = new byte[compressedLength];

        // Double-check, make sure native code round-trips
        var nativeCompressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096, CompressionFormat.Lznt1);
        Assert.Equal(_uncompressedData, NativeDecompress(nativeCompressed, 0, nativeCompressed.Length, CompressionFormat.Lznt1));

        compressor.BlockSize = 4096;
        var r = compressor.TryCompress(_uncompressedData, compressedData, out compressedLength);
        Assert.Equal(CompressionResult.Compressed, r);
        Assert.Equal(_uncompressedData, NativeDecompress(compressedData, 0, compressedLength, CompressionFormat.Lznt1));

        Assert.True(compressedLength < _uncompressedData.Length * 0.66);
    }

    [WindowsOnlyFact]
    public void CompressMidSourceBuffer()
    {
        var instance = LZNT1.Default;
        var compressor = instance;

        var inData = new byte[128 * 1024];
        Buffer.BlockCopy(_uncompressedData, 0, inData, 32 * 1024, 64 * 1024);

        var compressedLength = 16 * 4096;
        var compressedData = new byte[compressedLength];

        // Double-check, make sure native code round-trips
        var nativeCompressed = NativeCompress(inData, 32 * 1024, _uncompressedData.Length, 4096, CompressionFormat.Lznt1);
        Assert.Equal(_uncompressedData, NativeDecompress(nativeCompressed, 0, nativeCompressed.Length, CompressionFormat.Lznt1));

        compressor.BlockSize = 4096;
        var r = compressor.TryCompress(inData.AsSpan(32 * 1024, _uncompressedData.Length), compressedData, out compressedLength);
        Assert.Equal(CompressionResult.Compressed, r);
        Assert.Equal(_uncompressedData, NativeDecompress(compressedData, 0, compressedLength, CompressionFormat.Lznt1));
    }

    [WindowsOnlyFact]
    public void CompressMidDestBuffer()
    {
        var instance = LZNT1.Default;
        var compressor = instance;

        // Double-check, make sure native code round-trips
        var nativeCompressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096, CompressionFormat.Lznt1);
        Assert.Equal(_uncompressedData, NativeDecompress(nativeCompressed, 0, nativeCompressed.Length, CompressionFormat.Lznt1));

        var compressedLength = 128 * 1024;
        var compressedData = new byte[compressedLength];

        compressor.BlockSize = 4096;
        var r = compressor.TryCompress(_uncompressedData, compressedData.AsSpan(32 * 1024), out compressedLength);
        Assert.Equal(CompressionResult.Compressed, r);
        Assert.True(compressedLength < _uncompressedData.Length);

        Assert.Equal(_uncompressedData, NativeDecompress(compressedData, 32 * 1024, compressedLength, CompressionFormat.Lznt1));
    }

    [WindowsOnlyFact]
    public void Compress1KBlockSize()
    {
        var instance = LZNT1.Default;
        var compressor = instance;

        var compressedLength = 16 * 4096;
        var compressedData = new byte[compressedLength];

        // Double-check, make sure native code round-trips
        var nativeCompressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 1024, CompressionFormat.Lznt1);
        Assert.Equal(_uncompressedData, NativeDecompress(nativeCompressed, 0, nativeCompressed.Length, CompressionFormat.Lznt1));

        compressor.BlockSize = 1024;
        var r = compressor.TryCompress(_uncompressedData, compressedData, out compressedLength);
        Assert.Equal(CompressionResult.Compressed, r);

        var duDecompressed = new byte[_uncompressedData.Length];
        var rc = compressor.TryDecompress(compressedData.AsSpan(0, compressedLength), duDecompressed, out var numDuDecompressed);

        Assert.True(rc);

        var rightSizedDuDecompressed = new byte[numDuDecompressed];
        Buffer.BlockCopy(duDecompressed, 0, rightSizedDuDecompressed, 0, numDuDecompressed);

        // Note: Due to bug in Windows LZNT1, we compare against native decompression, not the original data, since
        // Windows LZNT1 corrupts data on decompression when block size != 4096.
        Assert.Equal(rightSizedDuDecompressed, NativeDecompress(compressedData, 0, compressedLength, CompressionFormat.Lznt1));
    }

    [WindowsOnlyFact]
    public void Compress1KBlock()
    {
        var instance = LZNT1.Default;
        var compressor = instance;

        var uncompressed1K = new byte[1024];
        Buffer.BlockCopy(_uncompressedData, 0, uncompressed1K, 0, 1024);

        var compressedLength = 1024;
        var compressedData = new byte[compressedLength];

        // Double-check, make sure native code round-trips
        var nativeCompressed = NativeCompress(uncompressed1K, 0, 1024, 1024, CompressionFormat.Lznt1);
        Assert.Equal(uncompressed1K, NativeDecompress(nativeCompressed, 0, nativeCompressed.Length, CompressionFormat.Lznt1));

        compressor.BlockSize = 1024;
        var r = compressor.TryCompress(uncompressed1K, compressedData, out compressedLength);
        Assert.Equal(CompressionResult.Compressed, r);
        Assert.Equal(uncompressed1K, NativeDecompress(compressedData, 0, compressedLength, CompressionFormat.Lznt1));
    }

    [Fact]
    public void CompressAllZeros()
    {
        var instance = LZNT1.Default;
        var compressor = instance;

        var compressed = new byte[64 * 1024];
        var numCompressed = 64 * 1024;
        Assert.Equal(CompressionResult.AllZeros, compressor.TryCompress(new byte[64 * 1024], compressed, out numCompressed));
    }

    [Fact]
    public void CompressIncompressible()
    {
        var instance = LZNT1.Default;
        var compressor = instance;

        var rng = new Random(6324);
        var uncompressed = new byte[64 * 1024];
        rng.NextBytes(uncompressed);

        var compressed = new byte[64 * 1024];
        var numCompressed = 64 * 1024;

        Assert.Equal(CompressionResult.Incompressible, compressor.TryCompress(uncompressed, compressed, out numCompressed));
    }

    [WindowsOnlyFact]
    public void Decompress()
    {
        var instance = LZNT1.Default;
        var compressor = instance;

        var compressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096, CompressionFormat.Lznt1);

        // Double-check, make sure native code round-trips
        Assert.Equal(_uncompressedData, NativeDecompress(compressed, 0, compressed.Length, CompressionFormat.Lznt1));

        var decompressed = new byte[_uncompressedData.Length];
        var rc = compressor.TryDecompress(compressed, decompressed, out var numDecompressed);
        Assert.True(rc);
        Assert.Equal(numDecompressed, _uncompressedData.Length);

        Assert.Equal(_uncompressedData, decompressed);
    }

    [WindowsOnlyFact]
    public void DecompressMidSourceBuffer()
    {
        var instance = LZNT1.Default;
        var compressor = instance;

        var compressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096, CompressionFormat.Lznt1);

        var inData = new byte[128 * 1024];
        Buffer.BlockCopy(compressed, 0, inData, 32 * 1024, compressed.Length);

        // Double-check, make sure native code round-trips
        Assert.Equal(_uncompressedData, NativeDecompress(inData, 32 * 1024, compressed.Length, CompressionFormat.Lznt1));

        var decompressed = new byte[_uncompressedData.Length];
        var rc = compressor.TryDecompress(inData.AsSpan(32 * 1024, compressed.Length), decompressed, out var numDecompressed);
        Assert.True(rc);
        Assert.Equal(numDecompressed, _uncompressedData.Length);

        Assert.Equal(_uncompressedData, decompressed);
    }

    [WindowsOnlyFact]
    public void DecompressMidDestBuffer()
    {
        var instance = LZNT1.Default;
        var compressor = instance;

        var compressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096, CompressionFormat.Lznt1);

        // Double-check, make sure native code round-trips
        Assert.Equal(_uncompressedData, NativeDecompress(compressed, 0, compressed.Length, CompressionFormat.Lznt1));

        var outData = new byte[128 * 1024];
        var rc = compressor.TryDecompress(compressed, outData.AsSpan(32 * 1024), out var numDecompressed);
        Assert.True(rc);
        Assert.Equal(numDecompressed, _uncompressedData.Length);

        var decompressed = new byte[_uncompressedData.Length];
        Buffer.BlockCopy(outData, 32 * 1024, decompressed, 0, _uncompressedData.Length);
        Assert.Equal(_uncompressedData, decompressed);
    }

    [WindowsOnlyFact]
    public void Decompress1KBlockSize()
    {
        var instance = LZNT1.Default;
        var compressor = instance;

        var compressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 1024, CompressionFormat.Lznt1);

        Assert.Equal(_uncompressedData, NativeDecompress(compressed, 0, compressed.Length, CompressionFormat.Lznt1));

        var decompressed = new byte[_uncompressedData.Length];
        var rc = compressor.TryDecompress(compressed, decompressed, out var numDecompressed);
        Assert.True(rc);
        Assert.Equal(numDecompressed, _uncompressedData.Length);

        Assert.Equal(_uncompressedData, decompressed);
    }
}
