using System;
using System.Buffers.Binary;
using System.IO;

namespace DiscUtils.Compression;

public sealed class XpressLz77 : IBlockDecompressor
{
    public static XpressLz77 Default => field ??= new();

    int IBlockDecompressor.BlockSize { get; set; }

    bool IBlockDecompressor.TryDecompress(ReadOnlySpan<byte> compressed, Span<byte> output, out int decompressedSize)
        => TryDecompress(compressed, output, out decompressedSize);

    /// <summary>
    /// Decompress XPRESS (Plain LZ77) stream into <paramref name="output"/>.
    /// </summary>
    /// <param name="compressed">Entire XPRESS block (no container header)</param>
    /// <param name="output">Buffer with expected decompressed size</param>
    /// <param name="decompressedSize"></param>
    /// <exception cref="InvalidDataException">On malformed input</exception>
    public static bool TryDecompress(ReadOnlySpan<byte> compressed, Span<byte> output, out int decompressedSize)
    {
        // We implement the MS-XCA “fastest variant / Plain LZ77” decoder.
        // Ref: MS-XCA §2.3.4 Processing (literal/match flags in 32-bit chunks; 16-bit match token). :contentReference[oaicite:3]{index=3}

        var expectedSize = output.Length;
        var src = 0;
        decompressedSize = 0;
        var cachedLenNibble = -1; // -1 = empty; otherwise 0..15

        while (decompressedSize < expectedSize)
        {
            if (src + 4 > compressed.Length)
            {
                return false;
            }

            // Flags are processed MSB → LSB (we write them, then consume from the high bit).
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(compressed.Slice(src, 4));
            src += 4;

            for (var i = 0; i < 32; i++)
            {
                if (decompressedSize >= expectedSize)
                {
                    break; // Done
                }

                var isMatch = (flags & 0x8000_0000u) != 0;
                flags <<= 1;

                if (!isMatch)
                {
                    // Literal
                    if (src >= compressed.Length)
                    {
                        return false;
                    }

                    output[decompressedSize++] = compressed[src++];

                    continue;
                }

                if (src == compressed.Length)
                {
                    return true;
                }
                
                // Match: first 2 bytes are the primary token
                if (src + 2 > compressed.Length)
                {
                    return false;
                }

                var token = BinaryPrimitives.ReadUInt16LittleEndian(compressed.Slice(src, 2));
                src += 2;

                var matchOffset = ((token >> 3) & 0x1FFF) + 1;   // 13 bits + bias
                var lenMinus3 = (token & 0x7);                 // 3 low bits
                int matchLen;

                if (lenMinus3 < 7)
                {
                    matchLen = lenMinus3 + 3;
                }
                else
                {
                    // Extended length: consume a 4-bit nibble that is packed 2-per-byte.
                    // We must reuse the high nibble on every second extended-length match.
                    int nibble;
                    if (cachedLenNibble >= 0)
                    {
                        nibble = cachedLenNibble;
                        cachedLenNibble = -1;
                    }
                    else
                    {
                        if (src >= compressed.Length)
                        {
                            return false;
                        }

                        var lenCtl = compressed[src++];
                        nibble = lenCtl & 0x0F;
                        cachedLenNibble = (lenCtl >> 4) & 0x0F;
                    }

                    if (nibble != 15)
                    {
                        matchLen = 3 + 7 + nibble;
                    }
                    else
                    {
                        if (src >= compressed.Length)
                        {
                            return false;
                        }

                        int b = compressed[src++];

                        if (b != 255)
                        {
                            matchLen = 3 + 7 + 15 + b;
                        }
                        else
                        {
                            if (src + 2 > compressed.Length)
                            {
                                return false;
                            }

                            var len16 = BinaryPrimitives.ReadUInt16LittleEndian(compressed.Slice(src, 2));
                            src += 2;

                            if (len16 != 0)
                            {
                                // For XPRESS, this is effectively "length minus 3" in many emitters.
                                matchLen = len16 + 3;
                            }
                            else
                            {
                                if (src + 4 > compressed.Length)
                                {
                                    return false;
                                }

                                var len32 = BinaryPrimitives.ReadInt32LittleEndian(compressed.Slice(src, 4));
                                src += 4;
                                matchLen = len32 + 3;
                            }
                        }
                    }
                }

                // Copy match
                if (matchOffset <= 0 || matchOffset > 8192)
                {
                    return false;
                }

                if (decompressedSize < matchOffset)
                {
                    return false;
                }

                // Bounds check (stream might claim more than remaining output)
                var toCopy = matchLen;
                if (decompressedSize + toCopy > expectedSize)
                {
                    return false;
                }

                var srcPos = decompressedSize - matchOffset;
                // Overlap-safe copy
                while (toCopy-- > 0)
                {
                    output[decompressedSize++] = output[srcPos++];
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Decompress XPRESS (Plain LZ77) stream into <paramref name="output"/>.
    /// </summary>
    /// <param name="compressed">Entire XPRESS block (no container header)</param>
    /// <param name="output">Buffer with decompressed size</param>
    /// <exception cref="InvalidDataException">On malformed input</exception>
    public static void Decompress(ReadOnlySpan<byte> compressed, Span<byte> output)
    {
        if (!TryDecompress(compressed, output, out var decompressedSize) || decompressedSize != output.Length)
        {
            throw new InvalidDataException("Malformed XPRESS compressed data");
        }
    }
}
