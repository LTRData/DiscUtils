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
using DiscUtils.Streams;

namespace DiscUtils.Compression;

/// <summary>
/// Implementation of the LZNT1 algorithm used for compressing NTFS files.
/// </summary>
/// <remarks>
/// Due to apparent bugs in Window's LZNT1 decompressor, it is <b>strongly</b> recommended that
/// only the block size of 4096 is used.  Other block sizes corrupt data on decompression.
/// </remarks>
internal sealed class LZNT1 : BlockCompressor
{
    private const ushort SubBlockIsCompressedFlag = 0x8000;
    private const ushort SubBlockSizeMask = 0x0fff;

    // LZNT1 appears to ignore the actual block size requested, most likely due to
    // a bug in the decompressor, which assumes 4KB block size.  To be bug-compatible,
    // we assume each block is 4KB on decode also.
    private const int FixedBlockSize = 0x1000;

    private static readonly byte[] _compressionBits = CalcCompressionBits();

    public LZNT1()
    {
        BlockSize = 4096;
    }

    public override CompressionResult Compress(ReadOnlySpan<byte> source, Span<byte> compressed,
                                               out int compressedLength)
    {
        var sourcePointer = 0;
        var destPointer = 0;
        compressedLength = compressed.Length;

        // Set up the Lz Compression Dictionary 
        var lzDictionary = new LzWindowDictionary();
        var nonZeroDataFound = false;

        for (var subBlock = 0; subBlock < source.Length; subBlock += BlockSize)
        {
            lzDictionary.MinMatchAmount = 3;
            var sourceCurrentBlock = sourcePointer;

            var decompressedSize = (uint)Math.Min(source.Length - subBlock, BlockSize);
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

                    lzDictionary.MaxMatchAmount = Math.Min(1 << lengthBits, BlockSize - 1);

                    var lzSearchMatch = lzDictionary.Search(source.Slice(subBlock),
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
                        EndianUtilities.WriteBytesLittleEndian(convertedData, compressed.Slice(destPointer));

                        lzDictionary.AddEntryRange(source.Slice(subBlock), sourcePointer - subBlock,
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
                        lzDictionary.AddEntry(source.Slice(subBlock), sourcePointer - subBlock);

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

            // If compressed size >= block size just store block
            if (compressedSize >= BlockSize)
            {
                // Set the header to indicate non-compressed block
                EndianUtilities.WriteBytesLittleEndian((ushort)(0x3000 | (BlockSize - 1)), compressed.Slice(
                    headerPosition));

                source.Slice(sourceCurrentBlock, BlockSize).CopyTo(compressed.Slice(headerPosition + 2));
                destPointer = headerPosition + 2 + BlockSize;

                // Make sure decompression stops by setting the next two bytes to null, prevents us from having to 
                // clear the rest of the array.
                compressed[destPointer] = 0;
                compressed[destPointer + 1] = 0;
            }
            else
            {
                // Set the header to indicate compressed and the right length
                EndianUtilities.WriteBytesLittleEndian((ushort)(0xb000 | (compressedSize - 1)), compressed.Slice(
                    headerPosition));
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

    public override int Decompress(ReadOnlySpan<byte> source, Span<byte> decompressed)
    {
        var sourceIdx = 0;
        var destIdx = 0;

        while (sourceIdx < source.Length)
        {
            var header = EndianUtilities.ToUInt16LittleEndian(source.Slice(sourceIdx));
            sourceIdx += 2;

            // Look for null-terminating sub-block header
            if (header == 0)
            {
                break;
            }

            if ((header & SubBlockIsCompressedFlag) == 0)
            {
                var blockSize = (header & SubBlockSizeMask) + 1;
                source.Slice(sourceIdx, blockSize).CopyTo(decompressed.Slice(destIdx));
                sourceIdx += blockSize;
                destIdx += blockSize;
            }
            else
            {
                // compressed
                var destSubBlockStart = destIdx;
                var srcSubBlockEnd = sourceIdx + (header & SubBlockSizeMask) + 1;
                while (sourceIdx < srcSubBlockEnd)
                {
                    var tag = source[sourceIdx];
                    ++sourceIdx;

                    for (var token = 0; token < 8; ++token)
                    {
                        // We might have hit the end of the sub block whilst still working though
                        // a tag - abort if we have...
                        if (sourceIdx >= srcSubBlockEnd)
                        {
                            break;
                        }

                        if ((tag & 1) == 0)
                        {
                            if (destIdx >= decompressed.Length)
                            {
                                return destIdx;
                            }

                            decompressed[destIdx] = source[sourceIdx];
                            ++destIdx;
                            ++sourceIdx;
                        }
                        else
                        {
                            var lengthBits = (ushort)(16 - _compressionBits[destIdx - destSubBlockStart]);
                            var lengthMask = (ushort)((1 << lengthBits) - 1);

                            var phraseToken = EndianUtilities.ToUInt16LittleEndian(source.Slice(sourceIdx));
                            sourceIdx += 2;

                            var destBackAddr = destIdx - (phraseToken >> lengthBits) - 1;
                            var length = (phraseToken & lengthMask) + 3;

                            for (var i = 0; i < length; ++i)
                            {
                                decompressed[destIdx++] =
                                    decompressed[destBackAddr++];
                            }
                        }

                        tag >>= 1;
                    }
                }

                // Bug-compatible - if we decompressed less than 4KB, jump to next 4KB boundary.  If
                // that would leave less than a 4KB remaining, abort with data decompressed so far.
                if (destIdx + FixedBlockSize > decompressed.Length)
                {
                    return destIdx;
                }

                if (destIdx < destSubBlockStart + FixedBlockSize)
                {
                    var skip = destSubBlockStart + FixedBlockSize - destIdx;
                    decompressed.Slice(destIdx, skip).Clear();
                    destIdx += skip;
                }
            }
        }

        return destIdx;
    }

    private static byte[] CalcCompressionBits()
    {
        var result = new byte[4096];
        byte offsetBits = 0;

        var y = 0x10;
        for (var x = 0; x < result.Length; x++)
        {
            result[x] = (byte)(4 + offsetBits);
            if (x == y)
            {
                y <<= 1;
                offsetBits++;
            }
        }

        return result;
    }
}