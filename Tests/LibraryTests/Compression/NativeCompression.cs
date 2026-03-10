using System;
using System.Runtime.InteropServices;

namespace LibraryTests.Compression;

internal static partial class NativeCompression
{
    /// <summary>
    /// Compression format identifiers used by Windows native compression APIs
    /// (e.g. RtlCompressBuffer / RtlDecompressBufferEx).
    /// </summary>
    public enum CompressionFormat : ushort
    {
        /// <summary>
        /// No compression.
        /// </summary>
        None = 0x0000,

        /// <summary>
        /// Default compression format.
        /// </summary>
        Default = 0x0001,

        /// <summary>
        /// LZNT1 compression format.
        /// </summary>
        Lznt1 = 0x0002,

        /// <summary>
        /// XPRESS compression format.
        /// </summary>
        Xpress = 0x0003,

        /// <summary>
        /// XPRESS with Huffman encoding.
        /// </summary>
        XpressHuff = 0x0004,

        /// <summary>
        /// XP10 compression format.
        /// </summary>
        Xp10 = 0x0005,

        /// <summary>
        /// LZ4 compression format.
        /// </summary>
        Lz4 = 0x0006,

        /// <summary>
        /// DEFLATE compression format.
        /// </summary>
        Deflate = 0x0007,

        /// <summary>
        /// ZLIB compression format.
        /// </summary>
        Zlib = 0x0008
    }

    public static byte[] NativeCompress(byte[] data, int offset, int length, int chunkSize, CompressionFormat compressionFormat)
    {
        var compressedBuffer = IntPtr.Zero;
        var uncompressedBuffer = IntPtr.Zero;
        var workspaceBuffer = IntPtr.Zero;
        try
        {
            uncompressedBuffer = Marshal.AllocHGlobal(length);
            Marshal.Copy(data, offset, uncompressedBuffer, length);

            compressedBuffer = Marshal.AllocHGlobal(length);

            var ntStatus = RtlGetCompressionWorkSpaceSize(compressionFormat, out var bufferWorkspaceSize, out var fragmentWorkspaceSize);

            Assert.Equal(0, ntStatus);

            workspaceBuffer = Marshal.AllocHGlobal((int)bufferWorkspaceSize);

            ntStatus = RtlCompressBuffer(compressionFormat, uncompressedBuffer, (uint)length, compressedBuffer, (uint)length, (uint)chunkSize, out var compressedSize, workspaceBuffer);
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

    public static byte[] NativeDecompress(byte[] data, int offset, int length, CompressionFormat compressionFormat)
    {
        var compressedBuffer = IntPtr.Zero;
        var uncompressedBuffer = IntPtr.Zero;
        try
        {
            compressedBuffer = Marshal.AllocHGlobal(length);
            Marshal.Copy(data, offset, compressedBuffer, length);

            uncompressedBuffer = Marshal.AllocHGlobal(64 * 1024);

            var ntStatus = RtlDecompressBuffer(compressionFormat, uncompressedBuffer, 64 * 1024, compressedBuffer, (uint)length, out var uncompressedSize);
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

#if NET7_0_OR_GREATER
    [LibraryImport("ntdll")]
    private static partial int RtlGetCompressionWorkSpaceSize(CompressionFormat formatAndEngine, out uint bufferWorkspaceSize, out uint fragmentWorkspaceSize);

    [LibraryImport("ntdll")]
    private static partial int RtlCompressBuffer(CompressionFormat formatAndEngine, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, uint uncompressedChunkSize, out uint finalCompressedSize, IntPtr workspace);

    [LibraryImport("ntdll")]
    private static partial int RtlDecompressBuffer(CompressionFormat formatAndEngine, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, out uint finalUncompressedSize);
#else
    [DllImport("ntdll")]
    private static extern int RtlGetCompressionWorkSpaceSize(CompressionFormat formatAndEngine, out uint bufferWorkspaceSize, out uint fragmentWorkspaceSize);

    [DllImport("ntdll")]
    private static extern int RtlCompressBuffer(CompressionFormat formatAndEngine, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, uint uncompressedChunkSize, out uint finalCompressedSize, IntPtr workspace);

    [DllImport("ntdll")]
    private static extern int RtlDecompressBuffer(CompressionFormat formatAndEngine, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, out uint finalUncompressedSize);
#endif
}
