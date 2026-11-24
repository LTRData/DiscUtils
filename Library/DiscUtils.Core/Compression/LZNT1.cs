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
/// compressed chunk as 4096 bytes, padding a shorter one with zeros. <see cref="BlockSize"/> is therefore fixed at 4096.
/// </remarks>
public sealed class LZNT1 : IBlockCompressor
{
    private const ushort SubBlockIsCompressedFlag = 0x8000;
    private const ushort SubBlockSizeMask = 0x0fff;

    // The uncompressed size of an LZNT1 chunk. The offset/length split of a back reference depends on the position within
    // the chunk (see CalcCompressionBits), which only covers 4096 positions.
    private const int FixedBlockSize = 0x1000;

    public static LZNT1 Default => field ??= new();

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
                EndianUtilities.WriteBytesLittleEndian((ushort)(0xb000 | (compressedSize - 1)), compressed[
                    headerPosition..]);
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
    /// last. A compressed chunk that decompresses to less than 4096 bytes and is followed by another chunk is padded with zeros, as Windows
    /// does. Decoding stops at a zero header, at the end of <paramref name="source"/> or when <paramref name="decompressed"/> is full.
    /// </summary>
    /// <returns><see langword="false"/> when the data is malformed (a chunk that runs past the input, or a back reference before the start of its chunk).</returns>
    public bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> decompressed, out int decompressedSize)
    {
        var sourceIdx = 0;
        decompressedSize = 0;

        while (sourceIdx + 2 <= source.Length && decompressedSize < decompressed.Length)
        {
            var header = EndianUtilities.ToUInt16LittleEndian(source[sourceIdx..]);
            sourceIdx += 2;

            // Look for null-terminating sub-block header
            if (header == 0)
            {
                break;
            }

            var chunkLength = (header & SubBlockSizeMask) + 1;
            if (chunkLength > source.Length - sourceIdx)
            {
                return false;
            }

            var chunkEnd = sourceIdx + chunkLength;
            var destSubBlockStart = decompressedSize;

            if ((header & SubBlockIsCompressedFlag) == 0)
            {
                var count = Math.Min(chunkLength, decompressed.Length - decompressedSize);
                source.Slice(sourceIdx, count).CopyTo(decompressed[decompressedSize..]);
                sourceIdx = chunkEnd;
                decompressedSize += count;
                continue;
            }

            while (sourceIdx < chunkEnd)
            {
                var tag = source[sourceIdx];
                ++sourceIdx;

                for (var token = 0; token < 8 && sourceIdx < chunkEnd; ++token, tag >>= 1)
                {
                    if (decompressedSize >= decompressed.Length || decompressedSize - destSubBlockStart >= FixedBlockSize)
                    {
                        // The output is full, or the chunk holds more than one chunk's worth of data: stop at its end.
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

                    var lengthBits = 16 - _compressionBits[decompressedSize - destSubBlockStart];
                    var lengthMask = (1 << lengthBits) - 1;

                    var phraseToken = EndianUtilities.ToUInt16LittleEndian(source[sourceIdx..]);
                    sourceIdx += 2;

                    var destBackAddr = decompressedSize - (phraseToken >> lengthBits) - 1;
                    if (destBackAddr < destSubBlockStart)
                    {
                        return false;
                    }

                    var length = Math.Min((phraseToken & lengthMask) + 3, decompressed.Length - decompressedSize);
                    for (var i = 0; i < length; ++i)
                    {
                        decompressed[decompressedSize++] = decompressed[destBackAddr++];
                    }
                }
            }

            // A compressed chunk that is followed by another one stands for a full 4096 bytes: pad the rest with zeros, as Windows
            // does. When the next chunk would start at or past the end of the output, Windows stops without padding; so does this.
            var nextHeader = sourceIdx + 2 <= source.Length ? EndianUtilities.ToUInt16LittleEndian(source[sourceIdx..]) : 0;
            var nextStart = destSubBlockStart + FixedBlockSize;
            if (nextHeader != 0 && decompressedSize < nextStart)
            {
                if (nextStart >= decompressed.Length)
                {
                    break;
                }

                decompressed[decompressedSize..nextStart].Clear();
                decompressedSize = nextStart;
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