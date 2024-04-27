//
// Copyright (c) 2008-2024, Kenneth Bell, Olof Lagerkvist and contributors
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

using DiscUtils.Streams;
using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;

namespace DiscUtils.Ntfs.Internals;

/// <summary>
/// Implementation of WOF compressed files, by Olof Lagerkvist 2024
/// Registers as a reparse plugin to NtfsFileSystem class
/// 
/// More about WofCompressedData: https://devblogs.microsoft.com/oldnewthing/20190618-00/?p=102597
/// </summary>
internal static class Wof
{
    public const uint ReparsePointTagWofCompressed = 0x80000017u;

    public enum CompressionFormat
    {
        XPress4K,
        LZX,
        XPress8K,
        XPress16K,
    }

    public readonly struct WofExternalInfo
    {
        public WofExternalInfo(ReadOnlySpan<byte> buffer)
        {
            version = EndianUtilities.ToInt32LittleEndian(buffer);
            provider = EndianUtilities.ToInt32LittleEndian(buffer.Slice(4));
            versionV1 = EndianUtilities.ToInt32LittleEndian(buffer.Slice(8));
            compressionFormat = (CompressionFormat)EndianUtilities.ToInt32LittleEndian(buffer.Slice(12));
        }

        public readonly int version;
        public readonly int provider;
        public readonly int versionV1;
        public readonly CompressionFormat compressionFormat;
    }

    public static SparseStream OpenWofReparsePoint(File file,
                                                   StandardInformation info,
                                                   NtfsAttribute attr,
                                                   FileAccess access,
                                                   NtfsStream ntfsStream,
                                                   ReparsePointRecord reparsePoint)
    {
        if (!info.FileAttributes.HasFlag(NtfsFileAttributes.SparseFile)
            || file.GetStream(AttributeType.Data, "WofCompressedData") is not { } compressedStream)
        {
            return null;
        }

        var metadata = new WofExternalInfo(reparsePoint.Content);

        var uncompressedSize = attr.Length;

        var chunkTableBitShift = uncompressedSize > uint.MaxValue ? 3 : 2;

        var chunkOrder = metadata.compressionFormat switch
        {
            CompressionFormat.XPress4K => 12,
            CompressionFormat.XPress8K => 13,
            CompressionFormat.XPress16K => 14,
            CompressionFormat.LZX => 15,
            _ => throw new NotImplementedException()
        };

        var chunkSize = 1 << chunkOrder;

        var numChunks = (int)((uncompressedSize + chunkSize - 1) >> chunkOrder);

        var chunkTableSize = (numChunks - 1) << chunkTableBitShift;

        var chunkTable = new long[numChunks - 1];

        var compressed = compressedStream.Open(FileAccess.Read);

        byte[] allocated = null;

        var chunkTableBytes = chunkTableSize >= 512
            ? (allocated = ArrayPool<byte>.Shared.Rent(chunkTableSize)).AsSpan(0, chunkTableSize)
            : stackalloc byte[chunkTableSize];

        try
        {
            compressed.ReadExactly(chunkTableBytes);

            if (chunkTableBitShift == 3)
            {
                for (var i = 0; i < numChunks - 1; i++)
                {
                    chunkTable[i] = EndianUtilities.ToInt64LittleEndian(chunkTableBytes.Slice(i * sizeof(long)));
                }
            }
            else
            {
                for (var i = 0; i < numChunks - 1; i++)
                {
                    chunkTable[i] = EndianUtilities.ToUInt32LittleEndian(chunkTableBytes.Slice(i * sizeof(uint)));
                }
            }
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }

        attr.PrimaryRecord.InitializedDataLength = uncompressedSize;

        var decompressStream = new WofStream(uncompressedSize,
                                             chunkOrder,
                                             chunkSize,
                                             numChunks,
                                             chunkTableSize,
                                             chunkTable,
                                             metadata.compressionFormat,
                                             compressed);

        var aligningStream = new AligningStream(decompressStream,
                                                Ownership.Dispose,
                                                chunkSize);

        // If opened for reading only, return this stream in place of primary stream to get seamless
        // decompressing behavior

        if (!access.HasFlag(FileAccess.Write))
        {
            return aligningStream;
        }

        // If opened for writing, we decompress all into primary stream and remove the compressed data stream

        var stream = attr.Open(access);
        
        aligningStream.CopyTo(stream);

        aligningStream.Dispose();

        file.RemoveStream(compressedStream);

        return stream;
    }
}
