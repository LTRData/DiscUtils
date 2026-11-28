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

//
// Contributions by bsobel:
//   - Compression algorithm distantly derived from Puyo tools (BSD license)*
//   - Decompression adjusted to support variety of block sizes
//
// (*) Puyo tools implements a different LZ-style algorithm
//

using System;
using System.Buffers;
using System.Collections.Immutable;
using DiscUtils.Streams;

namespace DiscUtils.Compression;

/// <summary>
/// Implementation of the LZNT1 algorithm used for compressing NTFS files.
/// </summary>
/// <remarks>
/// LZNT1 data is a sequence of chunks of 4096 uncompressed bytes each (the last one may be shorter). Windows works the same
/// way: <c>RtlCompressBuffer</c> ignores its chunk-size argument for LZNT1, and <c>RtlDecompressBuffer</c> treats every
/// chunk as 4096 bytes, padding a shorter one with zeros. <see cref="BlockSize"/> is therefore fixed at 4096. How data outside
/// the specification decodes is chosen by <see cref="Mode"/>: <see cref="Default"/> is <see cref="LZNT1Mode.Strict"/> and
/// <see cref="WindowsCompatible"/> is <see cref="LZNT1Mode.Windows"/>.
/// </remarks>
public sealed class LZNT1 : IBlockCompressor
{
    private const ushort SubBlockIsCompressedFlag = 0x8000;
    private const ushort SubBlockSignatureMask = 0x7000;
    private const ushort SubBlockSignature = 0x3000;
    private const ushort SubBlockSizeMask = 0x0fff;

    // The uncompressed size of an LZNT1 chunk. The offset/length split of a back reference depends on the position within
    // the chunk (see CalcCompressionBits), which only covers 4096 positions.
    private const int FixedBlockSize = 0x1000;

    /// <summary>A shared instance that decodes in <see cref="LZNT1Mode.Strict"/> mode.</summary>
    public static LZNT1 Default => field ??= new();

    /// <summary>A shared instance that decodes in <see cref="LZNT1Mode.Windows"/> mode, as <c>RtlDecompressBuffer</c> does.</summary>
    public static LZNT1 WindowsCompatible => field ??= new(LZNT1Mode.Windows);

    /// <summary>Creates an instance that decodes in <see cref="LZNT1Mode.Strict"/> mode.</summary>
    public LZNT1()
        : this(LZNT1Mode.Strict)
    {
    }

    /// <summary>Creates an instance that decodes in the given mode.</summary>
    public LZNT1(LZNT1Mode mode)
    {
        if (mode is not (LZNT1Mode.Strict or LZNT1Mode.Windows))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown LZNT1 mode.");
        }

        Mode = mode;
    }

    /// <summary>How data outside the specification decodes; compression does not depend on it.</summary>
    public LZNT1Mode Mode { get; }

    private static ImmutableArray<byte> _compressionBits => field.IsDefault ? field = CalcCompressionBits() : field;

    /// <summary>
    /// The uncompressed chunk size: always 4096. A value set is ignored, as Windows ignores the chunk size for LZNT1; data
    /// compressed in smaller chunks would decompress wrongly in Windows (each short compressed chunk is padded to 4096 bytes).
    /// </summary>
    public int BlockSize
    {
        get => FixedBlockSize;
        set { }
    }

    public CompressionResult TryCompress(ReadOnlySpan<byte> source, Span<byte> compressed, out int compressedLength)
    {
        var sourcePointer = 0;
        var destPointer = 0;
        compressedLength = compressed.Length;

        // Set up the Lz Compression Dictionary
        var lzDictionary = new LzWindowDictionary();
        var nonZeroDataFound = false;

        for (var subBlock = 0; subBlock < source.Length; subBlock += FixedBlockSize)
        {
            lzDictionary.MinMatchAmount = 3;
            var sourceCurrentBlock = sourcePointer;

            var decompressedSize = (uint)Math.Min(source.Length - subBlock, FixedBlockSize);
            uint compressedSize = 0;

            // Start compression
            if (destPointer + 2 > compressed.Length)
            {
                compressedLength = 0;
                return CompressionResult.Incompressible;
            }

            var headerPosition = destPointer;
            compressed[destPointer] = compressed[destPointer + 1] = 0;
            destPointer += 2;

            while (sourcePointer - subBlock < decompressedSize)
            {
                if (destPointer + 1 >= compressedLength)
                {
                    return CompressionResult.Incompressible;
                }

                byte bitFlag = 0x0;
                var flagPosition = destPointer;

                compressed[destPointer] = bitFlag; // It will be filled in later
                compressedSize++;
                destPointer++;

                for (var i = 0; i < 8; i++)
                {
                    var lengthBits = 16 - _compressionBits[sourcePointer - subBlock];
                    var lengthMask = (ushort)((1 << _compressionBits[sourcePointer - subBlock]) - 1);

                    lzDictionary.MaxMatchAmount = Math.Min(1 << lengthBits, FixedBlockSize - 1);

                    var lzSearchMatch = lzDictionary.Search(source[subBlock..],
                        sourcePointer - subBlock, decompressedSize);
                    if (lzSearchMatch.size > 0)
                    {
                        // There is a compression match
                        if (destPointer + 2 >= compressedLength)
                        {
                            return CompressionResult.Incompressible;
                        }

                        bitFlag |= (byte)(1 << i);

                        var rawOffset = lzSearchMatch.offset;
                        var rawLength = lzSearchMatch.size;

                        var convertedOffset = (rawOffset - 1) << lengthBits;
                        var convertedSize = (rawLength - 3) & ((1 << lengthMask) - 1);

                        var convertedData = (ushort)(convertedOffset | convertedSize);
                        EndianUtilities.WriteBytesLittleEndian(convertedData, compressed[destPointer..]);

                        lzDictionary.AddEntryRange(source[subBlock..], sourcePointer - subBlock,
                            lzSearchMatch.size);
                        sourcePointer += lzSearchMatch.size;
                        destPointer += 2;
                        compressedSize += 2;
                    }
                    else
                    {
                        // There wasn't a match
                        if (destPointer + 1 >= compressedLength)
                        {
                            return CompressionResult.Incompressible;
                        }

                        bitFlag |= (byte)(0 << i);

                        if (source[sourcePointer] != 0)
                        {
                            nonZeroDataFound = true;
                        }

                        compressed[destPointer] = source[sourcePointer];
                        lzDictionary.AddEntry(source[subBlock..], sourcePointer - subBlock);

                        sourcePointer++;
                        destPointer++;
                        compressedSize++;
                    }

                    // Check for out of bounds
                    if (sourcePointer - subBlock >= decompressedSize)
                    {
                        break;
                    }
                }

                // Write the real flag.
                compressed[flagPosition] = bitFlag;
            }

            // If the chunk did not shrink, store it uncompressed. The last chunk can be shorter than 4096 bytes, so the
            // stored length is the chunk's own length.
            if (compressedSize >= decompressedSize)
            {
                var storedLength = (int)decompressedSize;
                if (headerPosition + 2 + storedLength > compressed.Length)
                {
                    compressedLength = 0;
                    return CompressionResult.Incompressible;
                }

                // Set the header to indicate non-compressed block
                EndianUtilities.WriteBytesLittleEndian((ushort)(0x3000 | (storedLength - 1)), compressed[
                    headerPosition..]);

                source.Slice(sourceCurrentBlock, storedLength).CopyTo(compressed[(headerPosition + 2)..]);
                destPointer = headerPosition + 2 + storedLength;

                // Make sure decompression stops by setting the next two bytes to null, prevents us from having to
                // clear the rest of the array.
                if (destPointer + 2 <= compressed.Length)
                {
                    compressed[destPointer] = 0;
                    compressed[destPointer + 1] = 0;
                }
            }
            else
            {
                // Set the header to indicate compressed and the right length
                EndianUtilities.WriteBytesLittleEndian((ushort)(0xb000 | (compressedSize - 1)), compressed[headerPosition..]);
            }

            lzDictionary.Reset();
        }

        if (destPointer >= source.Length)
        {
            compressedLength = 0;
            return CompressionResult.Incompressible;
        }

        if (nonZeroDataFound)
        {
            compressedLength = destPointer;
            return CompressionResult.Compressed;
        }

        compressedLength = 0;
        return CompressionResult.AllZeros;
    }

    /// <summary>
    /// Decompresses LZNT1 data ([MS-XCA] 2.5): a sequence of chunks, each with a 16-bit header, that decompress to 4096 bytes each except the
    /// last. Decoding stops at a zero header, at the end of <paramref name="source"/> or when <paramref name="decompressed"/> is full.
    /// A single byte left after the last chunk is ignored. Data outside the specification decodes as <see cref="Mode"/> says.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the data is malformed: a chunk that runs past the input, a back reference cut off by the end of its
    /// chunk or reaching before the start of its chunk, and the cases <see cref="Mode"/> lists.
    /// </returns>
    public bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> decompressed, out int decompressedSize)
    {
        var windows = Mode == LZNT1Mode.Windows;
        var sourceIdx = 0;
        decompressedSize = 0;

        // Where the next chunk's output starts: after the previous chunk, or at the next 4096-byte boundary after a short one.
        var nextChunkStart = 0;
        var firstChunk = true;

        // As in Windows, decoding goes on while the next chunk would start inside the output; the first chunk's header and size
        // are always checked, even for an empty output.
        while (firstChunk || nextChunkStart < decompressed.Length)
        {
            if (sourceIdx + 2 > source.Length)
            {
                // Windows reads the first header unconditionally, so input shorter than a header fails.
                if (windows && firstChunk)
                {
                    return false;
                }

                break;
            }

            var header = EndianUtilities.ToUInt16LittleEndian(source[sourceIdx..]);
            sourceIdx += 2;

            // A zero header is the End_of_buffer terminal ([MS-XCA] 2.5.1.2). Windows reads a zero first header as a 1-byte
            // uncompressed chunk.
            if (header == 0 && !(windows && firstChunk))
            {
                break;
            }

            firstChunk = false;

            // Bits 12 to 14 are the chunk signature, which "MUST always be 3" ([MS-XCA] 2.5.1.2). Windows does not check it.
            if (!windows && (header & SubBlockSignatureMask) != SubBlockSignature)
            {
                return false;
            }

            var chunkLength = (header & SubBlockSizeMask) + 1;
            if (chunkLength > source.Length - sourceIdx)
            {
                return false;
            }

            // An empty output: the first chunk was only checked.
            if (nextChunkStart >= decompressed.Length)
            {
                break;
            }

            decompressed[decompressedSize..nextChunkStart].Clear();
            decompressedSize = nextChunkStart;

            var chunkEnd = sourceIdx + chunkLength;
            var destSubBlockStart = decompressedSize;

            if ((header & SubBlockIsCompressedFlag) == 0)
            {
                var count = Math.Min(chunkLength, decompressed.Length - decompressedSize);
                source.Slice(sourceIdx, count).CopyTo(decompressed[decompressedSize..]);
                sourceIdx = chunkEnd;
                decompressedSize += count;
            }

            while (sourceIdx < chunkEnd)
            {
                var tag = source[sourceIdx];
                ++sourceIdx;

                for (var token = 0; token < 8 && sourceIdx < chunkEnd; ++token, tag >>= 1)
                {
                    if (decompressedSize >= decompressed.Length)
                    {
                        // The output is full: stop at the end of the chunk.
                        sourceIdx = chunkEnd;
                        break;
                    }

                    if ((tag & 1) == 0)
                    {
                        decompressed[decompressedSize++] = source[sourceIdx++];
                        continue;
                    }

                    if (sourceIdx + 2 > chunkEnd)
                    {
                        return false;
                    }

                    // A chunk is compressed until "at least 4096 bytes" are in it ([MS-XCA] 2.5.3), so it may hold more; past
                    // 4096 bytes the split stays at 12 displacement bits, the most there are, as in Windows.
                    var lengthBits = 16 - _compressionBits[Math.Min(decompressedSize - destSubBlockStart, FixedBlockSize - 1)];
                    var lengthMask = (1 << lengthBits) - 1;

                    var phraseToken = EndianUtilities.ToUInt16LittleEndian(source[sourceIdx..]);
                    sourceIdx += 2;

                    var destBackAddr = decompressedSize - (phraseToken >> lengthBits) - 1;
                    if (destBackAddr < destSubBlockStart)
                    {
                        return false;
                    }

                    var length = (phraseToken & lengthMask) + 3;
                    if (length > decompressed.Length - decompressedSize)
                    {
                        // Windows fails on a back reference that does not fit the output; strict mode fills the output.
                        if (windows)
                        {
                            return false;
                        }

                        length = decompressed.Length - decompressedSize;
                    }

                    for (var i = 0; i < length; ++i)
                    {
                        decompressed[decompressedSize++] = decompressed[destBackAddr++];
                    }
                }
            }

            // A chunk followed by another one stands for at least 4096 bytes ("input streams are compressed in units of 4096
            // bytes", [MS-XCA] 2.5.3). Strict mode rejects a shorter one, unless the output filled up inside it. Windows pads it
            // with zeros, but stops without padding (and without reading the next chunk) when that starts at or past the end of
            // the output.
            nextChunkStart = decompressedSize;
            if (decompressedSize - destSubBlockStart < FixedBlockSize && decompressedSize < decompressed.Length
                && sourceIdx + 2 <= source.Length && EndianUtilities.ToUInt16LittleEndian(source[sourceIdx..]) != 0)
            {
                if (!windows)
                {
                    return false;
                }

                nextChunkStart = destSubBlockStart + FixedBlockSize;
            }
        }

        return true;
    }

    private static ImmutableArray<byte> CalcCompressionBits()
    {
        var result = ArrayPool<byte>.Shared.Rent(4096);

        try
        {
            byte offsetBits = 0;

            var y = 0x10;
            for (var x = 0; x < 4096; x++)
            {
                result[x] = (byte)(4 + offsetBits);
                if (x == y)
                {
                    y <<= 1;
                    offsetBits++;
                }
            }

            return ImmutableArray.Create(result, 0, 4096);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(result);
        }
    }
}