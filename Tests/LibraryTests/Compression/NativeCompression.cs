using System;
using System.Linq;
using System.Text;
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

    /// <summary>The compression engine flag for <see cref="CompressionFormat"/>; the standard engine is 0.</summary>
    public const ushort EngineMaximum = 0x0100;

    /// <summary>Compresses with Windows' compressor and fails the test when Windows does not compress the data (see <see cref="TryNativeCompress"/>).</summary>
    public static byte[] NativeCompress(byte[] data, int offset, int length, int chunkSize, CompressionFormat compressionFormat, ushort engine = 0)
    {
        var compressed = TryNativeCompress(data, offset, length, chunkSize, compressionFormat, engine);
        Assert.NotNull(compressed);
        return compressed!;
    }

    /// <summary>
    /// Compresses <paramref name="length"/> bytes of <paramref name="data"/> at <paramref name="offset"/> with Windows' compressor
    /// (<c>RtlCompressBuffer</c>), or returns null when Windows does not compress them: LZNT1 reports an all-zero buffer as such, and XPRESS
    /// refuses a 1-byte buffer as too small for its output. The output buffer leaves room for data that grows when compressed.
    /// </summary>
    public static byte[]? TryNativeCompress(byte[] data, int offset, int length, int chunkSize, CompressionFormat compressionFormat, ushort engine = 0)
    {
        const int StatusBufferAllZeros = 0x117;
        const int StatusBufferTooSmall = unchecked((int)0xC0000023);

        var formatAndEngine = (CompressionFormat)((ushort)compressionFormat | engine);
        var compressedLength = length * 2 + 65536;
        var compressedBuffer = IntPtr.Zero;
        var uncompressedBuffer = IntPtr.Zero;
        var workspaceBuffer = IntPtr.Zero;
        try
        {
            uncompressedBuffer = Marshal.AllocHGlobal(Math.Max(1, length));
            Marshal.Copy(data, offset, uncompressedBuffer, length);

            compressedBuffer = Marshal.AllocHGlobal(compressedLength);

            var ntStatus = RtlGetCompressionWorkSpaceSize(formatAndEngine, out var bufferWorkspaceSize, out _);
            Assert.Equal(0, ntStatus);

            workspaceBuffer = Marshal.AllocHGlobal((int)bufferWorkspaceSize);

            ntStatus = RtlCompressBuffer(formatAndEngine, uncompressedBuffer, (uint)length, compressedBuffer, (uint)compressedLength, (uint)chunkSize, out var compressedSize, workspaceBuffer);
            if (ntStatus is StatusBufferAllZeros or StatusBufferTooSmall)
            {
                return null;
            }

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

    /// <summary>Kinds and sizes of data for round trips through Windows' compressor: around the 4 KiB chunk and 64 KiB block sizes, and larger.</summary>
    public static TheoryData<string, int> RoundTripSamples => new()
    {
        { "text", 1 }, { "text", 4095 }, { "text", 65535 }, { "text", 65536 }, { "text", 65537 }, { "text", 70_000 }, { "text", 300_000 },
        { "random", 4096 }, { "random", 200_000 }, { "zeros", 150_000 }, { "mixed", 65537 }, { "mixed", 260_000 }
    };

    /// <summary>Reproducible data: repetitive text, random bytes, zeros, or random bytes with filled runs and a repeated quarter ("mixed").</summary>
    public static byte[] Sample(string kind, int length)
    {
        var random = new Random(length);
        var bytes = new byte[length];
        switch (kind)
        {
            case "text":
                var text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, length / 10 + 2).Select(i => $"line {i % 977} of text\r\n")));
                Array.Copy(text, bytes, length);
                break;
            case "random":
                random.NextBytes(bytes);
                break;
            case "mixed":
                random.NextBytes(bytes);
                for (var i = 0; i + 1000 < length; i += 3000)
                {
                    bytes.AsSpan(i, 1000).Fill((byte)(i / 3000));
                }

                bytes.AsSpan(0, length / 4).CopyTo(bytes.AsSpan(length / 2));
                break;
        }

        return bytes;
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

    /// <summary>
    /// Decompresses <paramref name="data"/> with Windows' decompressor into an output buffer of <paramref name="outputSize"/> bytes
    /// and returns its NTSTATUS, so malformed input and short outputs can be compared too. LZNT1 uses <c>RtlDecompressBuffer</c>;
    /// the XPRESS formats use <c>RtlDecompressBufferEx</c> with a workspace, since <c>RtlDecompressBuffer</c> refuses them with
    /// STATUS_INVALID_USER_BUFFER.
    /// </summary>
    public static int NativeDecompressStatus(byte[] data, int outputSize, CompressionFormat compressionFormat, out byte[] output)
    {
        var compressedBuffer = IntPtr.Zero;
        var uncompressedBuffer = IntPtr.Zero;
        var workspaceBuffer = IntPtr.Zero;
        try
        {
            compressedBuffer = Marshal.AllocHGlobal(Math.Max(1, data.Length));
            Marshal.Copy(data, 0, compressedBuffer, data.Length);
            uncompressedBuffer = Marshal.AllocHGlobal(Math.Max(1, outputSize));

            int ntStatus;
            uint uncompressedSize;
            if (compressionFormat == CompressionFormat.Lznt1)
            {
                ntStatus = RtlDecompressBuffer(compressionFormat, uncompressedBuffer, (uint)outputSize, compressedBuffer, (uint)data.Length, out uncompressedSize);
            }
            else
            {
                ntStatus = RtlGetCompressionWorkSpaceSize(compressionFormat, out var bufferWorkspaceSize, out var fragmentWorkspaceSize);
                Assert.Equal(0, ntStatus);
                workspaceBuffer = Marshal.AllocHGlobal((int)Math.Max(1, Math.Max(bufferWorkspaceSize, fragmentWorkspaceSize)));
                ntStatus = RtlDecompressBufferEx(compressionFormat, uncompressedBuffer, (uint)outputSize, compressedBuffer, (uint)data.Length, out uncompressedSize, workspaceBuffer);
            }

            output = new byte[ntStatus == 0 ? uncompressedSize : 0];
            Marshal.Copy(uncompressedBuffer, output, 0, output.Length);
            return ntStatus;
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

#if NET7_0_OR_GREATER
    [LibraryImport("ntdll")]
    private static partial int RtlGetCompressionWorkSpaceSize(CompressionFormat formatAndEngine, out uint bufferWorkspaceSize, out uint fragmentWorkspaceSize);

    [LibraryImport("ntdll")]
    private static partial int RtlCompressBuffer(CompressionFormat formatAndEngine, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, uint uncompressedChunkSize, out uint finalCompressedSize, IntPtr workspace);

    [LibraryImport("ntdll")]
    private static partial int RtlDecompressBuffer(CompressionFormat formatAndEngine, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, out uint finalUncompressedSize);

    [LibraryImport("ntdll")]
    private static partial int RtlDecompressBufferEx(CompressionFormat format, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, out uint finalUncompressedSize, IntPtr workspace);
#else
    [DllImport("ntdll")]
    private static extern int RtlGetCompressionWorkSpaceSize(CompressionFormat formatAndEngine, out uint bufferWorkspaceSize, out uint fragmentWorkspaceSize);

    [DllImport("ntdll")]
    private static extern int RtlCompressBuffer(CompressionFormat formatAndEngine, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, uint uncompressedChunkSize, out uint finalCompressedSize, IntPtr workspace);

    [DllImport("ntdll")]
    private static extern int RtlDecompressBuffer(CompressionFormat formatAndEngine, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, out uint finalUncompressedSize);

    [DllImport("ntdll")]
    private static extern int RtlDecompressBufferEx(CompressionFormat format, IntPtr uncompressedBuffer, uint uncompressedBufferSize, IntPtr compressedBuffer, uint compressedBufferSize, out uint finalUncompressedSize, IntPtr workspace);
#endif
}