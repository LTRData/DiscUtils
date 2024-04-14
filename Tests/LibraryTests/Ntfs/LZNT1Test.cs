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
using System.Runtime.InteropServices;
using DiscUtils.Compression;
using DiscUtils.Ntfs;
using Xunit;

namespace LibraryTests.Ntfs;

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

    private static object CreateInstance<T>(string name)
    {
#if NETCOREAPP1_1
        return typeof(T).GetTypeInfo().Assembly.CreateInstance(name);
#else
        return typeof(T).Assembly.CreateInstance(name);
#endif
    }

    [WindowsOnlyFact]
    public void Compress()
    {
        var instance = CreateInstance<NtfsFileSystem>("DiscUtils.Ntfs.LZNT1");
        var compressor = (BlockCompressor)instance;

        var compressedLength = 16 * 4096;
        var compressedData = new byte[compressedLength];

        // Double-check, make sure native code round-trips
        var nativeCompressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096);
        Assert.Equal(_uncompressedData, NativeDecompress(nativeCompressed, 0, nativeCompressed.Length));

        compressor.BlockSize = 4096;
        var r = compressor.Compress(_uncompressedData, compressedData, out compressedLength);
        Assert.Equal(CompressionResult.Compressed, r);
        Assert.Equal(_uncompressedData, NativeDecompress(compressedData, 0, compressedLength));

        Assert.True(compressedLength < _uncompressedData.Length * 0.66);
    }

    [WindowsOnlyFact]
    public void CompressMidSourceBuffer()
    {
        var instance = CreateInstance<NtfsFileSystem>("DiscUtils.Ntfs.LZNT1");
        var compressor = (BlockCompressor)instance;

        var inData = new byte[128 * 1024];
        Buffer.BlockCopy(_uncompressedData, 0, inData, 32 * 1024, 64 * 1024);

        var compressedLength = 16 * 4096;
        var compressedData = new byte[compressedLength];

        // Double-check, make sure native code round-trips
        var nativeCompressed = NativeCompress(inData, 32 * 1024, _uncompressedData.Length, 4096);
        Assert.Equal(_uncompressedData, NativeDecompress(nativeCompressed, 0, nativeCompressed.Length));

        compressor.BlockSize = 4096;
        var r = compressor.Compress(inData.AsSpan(32 * 1024, _uncompressedData.Length), compressedData, out compressedLength);
        Assert.Equal(CompressionResult.Compressed, r);
        Assert.Equal(_uncompressedData, NativeDecompress(compressedData, 0, compressedLength));
    }

    [WindowsOnlyFact]
    public void CompressMidDestBuffer()
    {
        var instance = CreateInstance<NtfsFileSystem>("DiscUtils.Ntfs.LZNT1");
        var compressor = (BlockCompressor)instance;

        // Double-check, make sure native code round-trips
        var nativeCompressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096);
        Assert.Equal(_uncompressedData, NativeDecompress(nativeCompressed, 0, nativeCompressed.Length));

        var compressedLength = 128 * 1024;
        var compressedData = new byte[compressedLength];

        compressor.BlockSize = 4096;
        var r = compressor.Compress(_uncompressedData, compressedData.AsSpan(32 * 1024), out compressedLength);
        Assert.Equal(CompressionResult.Compressed, r);
        Assert.True(compressedLength < _uncompressedData.Length);

        Assert.Equal(_uncompressedData, NativeDecompress(compressedData, 32 * 1024, compressedLength));
    }

    [WindowsOnlyFact]
    public void Compress1KBlockSize()
    {
        var instance = CreateInstance<NtfsFileSystem>("DiscUtils.Ntfs.LZNT1");
        var compressor = (BlockCompressor)instance;

        var compressedLength = 16 * 4096;
        var compressedData = new byte[compressedLength];

        // Double-check, make sure native code round-trips
        var nativeCompressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 1024);
        Assert.Equal(_uncompressedData, NativeDecompress(nativeCompressed, 0, nativeCompressed.Length));

        compressor.BlockSize = 1024;
        var r = compressor.Compress(_uncompressedData, compressedData, out compressedLength);
        Assert.Equal(CompressionResult.Compressed, r);

        var duDecompressed = new byte[_uncompressedData.Length];
        var numDuDecompressed = compressor.Decompress(compressedData.AsSpan(0, compressedLength), duDecompressed);

        var rightSizedDuDecompressed = new byte[numDuDecompressed];
        Buffer.BlockCopy(duDecompressed, 0, rightSizedDuDecompressed, 0, numDuDecompressed);

        // Note: Due to bug in Windows LZNT1, we compare against native decompression, not the original data, since
        // Windows LZNT1 corrupts data on decompression when block size != 4096.
        Assert.Equal(rightSizedDuDecompressed, NativeDecompress(compressedData, 0, compressedLength));
    }

    [WindowsOnlyFact]
    public void Compress1KBlock()
    {
        var instance = CreateInstance<NtfsFileSystem>("DiscUtils.Ntfs.LZNT1");
        var compressor = (BlockCompressor)instance;

        var uncompressed1K = new byte[1024];
        Buffer.BlockCopy(_uncompressedData, 0, uncompressed1K, 0, 1024);

        var compressedLength = 1024;
        var compressedData = new byte[compressedLength];

        // Double-check, make sure native code round-trips
        var nativeCompressed = NativeCompress(uncompressed1K, 0, 1024, 1024);
        Assert.Equal(uncompressed1K, NativeDecompress(nativeCompressed, 0, nativeCompressed.Length));

        compressor.BlockSize = 1024;
        var r = compressor.Compress(uncompressed1K, compressedData, out compressedLength);
        Assert.Equal(CompressionResult.Compressed, r);
        Assert.Equal(uncompressed1K, NativeDecompress(compressedData, 0, compressedLength));
    }

    [Fact]
    public void CompressAllZeros()
    {
        var instance = CreateInstance<NtfsFileSystem>("DiscUtils.Ntfs.LZNT1");
        var compressor = (BlockCompressor)instance;

        var compressed = new byte[64 * 1024];
        var numCompressed = 64 * 1024;
        Assert.Equal(CompressionResult.AllZeros, compressor.Compress(new byte[64 * 1024], compressed, out numCompressed));
    }

    [Fact]
    public void CompressIncompressible()
    {
        var instance = CreateInstance<NtfsFileSystem>("DiscUtils.Ntfs.LZNT1");
        var compressor = (BlockCompressor)instance;

        var rng = new Random(6324);
        var uncompressed = new byte[64 * 1024];
        rng.NextBytes(uncompressed);

        var compressed = new byte[64 * 1024];
        var numCompressed = 64 * 1024;
        Assert.Equal(CompressionResult.Incompressible, compressor.Compress(uncompressed, compressed, out numCompressed));
    }

    [WindowsOnlyFact]
    public void Decompress()
    {
        var instance = CreateInstance<NtfsFileSystem>("DiscUtils.Ntfs.LZNT1");
        var compressor = (BlockCompressor)instance;

        var compressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096);

        // Double-check, make sure native code round-trips
        Assert.Equal(_uncompressedData, NativeDecompress(compressed, 0, compressed.Length));

        var decompressed = new byte[_uncompressedData.Length];
        var numDecompressed = compressor.Decompress(compressed, decompressed);
        Assert.Equal(numDecompressed, _uncompressedData.Length);

        Assert.Equal(_uncompressedData, decompressed);
    }

    [WindowsOnlyFact]
    public void DecompressMidSourceBuffer()
    {
        var instance = CreateInstance<NtfsFileSystem>("DiscUtils.Ntfs.LZNT1");
        var compressor = (BlockCompressor)instance;

        var compressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096);

        var inData = new byte[128 * 1024];
        Buffer.BlockCopy(compressed, 0, inData, 32 * 1024, compressed.Length);

        // Double-check, make sure native code round-trips
        Assert.Equal(_uncompressedData, NativeDecompress(inData, 32 * 1024, compressed.Length));

        var decompressed = new byte[_uncompressedData.Length];
        var numDecompressed = compressor.Decompress(inData.AsSpan(32 * 1024, compressed.Length), decompressed);
        Assert.Equal(numDecompressed, _uncompressedData.Length);

        Assert.Equal(_uncompressedData, decompressed);
    }

    [WindowsOnlyFact]
    public void DecompressMidDestBuffer()
    {
        var instance = CreateInstance<NtfsFileSystem>("DiscUtils.Ntfs.LZNT1");
        var compressor = (BlockCompressor)instance;

        var compressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 4096);

        // Double-check, make sure native code round-trips
        Assert.Equal(_uncompressedData, NativeDecompress(compressed, 0, compressed.Length));

        var outData = new byte[128 * 1024];
        var numDecompressed = compressor.Decompress(compressed, outData.AsSpan(32 * 1024));
        Assert.Equal(numDecompressed, _uncompressedData.Length);

        var decompressed = new byte[_uncompressedData.Length];
        Buffer.BlockCopy(outData, 32 * 1024, decompressed, 0, _uncompressedData.Length);
        Assert.Equal(_uncompressedData, decompressed);
    }

    [WindowsOnlyFact]
    public void Decompress1KBlockSize()
    {
        var instance = CreateInstance<NtfsFileSystem>("DiscUtils.Ntfs.LZNT1");
        var compressor = (BlockCompressor)instance;

        var compressed = NativeCompress(_uncompressedData, 0, _uncompressedData.Length, 1024);

        Assert.Equal(_uncompressedData, NativeDecompress(compressed, 0, compressed.Length));

        var decompressed = new byte[_uncompressedData.Length];
        var numDecompressed = compressor.Decompress(compressed, decompressed);
        Assert.Equal(numDecompressed, _uncompressedData.Length);

        Assert.Equal(_uncompressedData, decompressed);
    }

    private static byte[] NativeCompress(byte[] data, int offset, int length, int chunkSize)
    {
        var compressedBuffer = IntPtr.Zero;
        var uncompressedBuffer = IntPtr.Zero;
        var workspaceBuffer = IntPtr.Zero;
        try
        {
            uncompressedBuffer = Marshal.AllocHGlobal(length);
            Marshal.Copy(data, offset, uncompressedBuffer, length);

            compressedBuffer = Marshal.AllocHGlobal(length);

            var ntStatus = RtlGetCompressionWorkSpaceSize(2, out var bufferWorkspaceSize, out var fragmentWorkspaceSize);

            Assert.Equal(0, ntStatus);

            workspaceBuffer = Marshal.AllocHGlobal((int)bufferWorkspaceSize);

            ntStatus = RtlCompressBuffer(2, uncompressedBuffer, (uint)length, compressedBuffer, (uint)length, (uint)chunkSize, out var compressedSize, workspaceBuffer);
            Assert.Equal(0, ntStatus);

            var result = new byte[compressedSize];

            Marshal.Copy(compressedBuffer, result, 0, (int)compressedSize);

            return result;
        }
        finally
        {
            if (compressedBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(compressedBuffer);
            }

            if (uncompressedBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(uncompressedBuffer);
            }

            if (workspaceBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(workspaceBuffer);
            }
        }
    }

    private static byte[] NativeDecompress(byte[] data, int offset, int length)
    {
        var compressedBuffer = IntPtr.Zero;
        var uncompressedBuffer = IntPtr.Zero;
        try
        {
            compressedBuffer = Marshal.AllocHGlobal(length);
            Marshal.Copy(data, offset, compressedBuffer, length);

            uncompressedBuffer = Marshal.AllocHGlobal(64 * 1024);

            var ntStatus = RtlDecompressBuffer(2, uncompressedBuffer, 64 * 1024, compressedBuffer, (uint)length, out var uncompressedSize);
            Assert.Equal(0, ntStatus);

            var result = new byte[uncompressedSize];

            Marshal.Copy(uncompressedBuffer, result, 0, (int)uncompressedSize);

            return result;
        }
        finally
        {
            if (compressedBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(compressedBuffer);
            }

            if (uncompressedBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(uncompressedBuffer);
            }
        }
    }

    [DllImport("ntdll")]
    private extern static int RtlGetCompressionWorkSpaceSize(ushort formatAndEngine, out uint bufferWorkspaceSize, out uint fragmentWorkspaceSize);

    [DllImport("ntdll")]
    private extern static int RtlCompressBuffer(ushort formatAndEngine, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, uint uncompressedChunkSize, out uint finalCompressedSize, IntPtr workspace);

    [DllImport("ntdll")]
    private extern static int RtlDecompressBuffer(ushort formatAndEngine, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, out uint finalUncompressedSize);
}
