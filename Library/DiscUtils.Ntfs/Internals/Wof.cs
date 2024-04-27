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

        var metadata = MemoryMarshal.Read<WofExternalInfo>(reparsePoint.Content);

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

        Span<byte> chunkTableBytes = stackalloc byte[chunkTableSize];

        var compressed = compressedStream.Open(FileAccess.Read);

        compressed.ReadExactly(chunkTableBytes);

        var chunkTable = chunkTableBitShift == 3
            ? MemoryMarshal.Cast<byte, long>(chunkTableBytes).ToArray()
            : Array.ConvertAll(MemoryMarshal.Cast<byte, uint>(chunkTableBytes).ToArray(), i => (long)i);

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
