using System;
using System.Buffers.Binary;
using System.IO;

namespace DiscUtils.Compression;

public sealed class XpressLz77 : IBlockDecompressor
{
    /// <summary>A decompressor in <see cref="XpressLz77Mode.Windows"/> mode.</summary>
    public static XpressLz77 WindowsCompatible => field ??= new();

    /// <summary>A decompressor in <see cref="XpressLz77Mode.Strict"/> mode.</summary>
    public static XpressLz77 Default => field ??= new(XpressLz77Mode.Strict);

    public XpressLz77(XpressLz77Mode mode = XpressLz77Mode.Windows)
    {
        Mode = mode;
    }

    /// <summary>How this instance treats input on which Windows and [MS-XCA] disagree.</summary>
    public XpressLz77Mode Mode { get; }

    int IBlockDecompressor.BlockSize { get; set; }

    bool IBlockDecompressor.TryDecompress(ReadOnlySpan<byte> compressed, Span<byte> output, out int decompressedSize)
        => TryDecompress(compressed, output, out decompressedSize, Mode);

    /// <summary>
    /// Decompress XPRESS (Plain LZ77) stream into <paramref name="output"/>, as Windows does.
    /// </summary>
    /// <param name="compressed">Entire XPRESS block (no container header)</param>
    /// <param name="output">Buffer at least as large as the decompressed data</param>
    /// <param name="decompressedSize">The number of bytes written to <paramref name="output"/></param>
    /// <returns><see langword="false"/> if the data is malformed or does not fit <paramref name="output"/>.</returns>
    public static bool TryDecompress(ReadOnlySpan<byte> compressed, Span<byte> output, out int decompressedSize)
        => TryDecompress(compressed, output, out decompressedSize, XpressLz77Mode.Windows);

    /// <summary>
    /// Decompress XPRESS (Plain LZ77) stream into <paramref name="output"/>.
    /// </summary>
    /// <param name="compressed">Entire XPRESS block (no container header)</param>
    /// <param name="output">Buffer at least as large as the decompressed data</param>
    /// <param name="decompressedSize">The number of bytes written to <paramref name="output"/></param>
    /// <param name="mode">How to treat input on which Windows and [MS-XCA] disagree</param>
    /// <returns><see langword="false"/> if the data is malformed or does not fit <paramref name="output"/>.</returns>
    public static bool TryDecompress(ReadOnlySpan<byte> compressed, Span<byte> output, out int decompressedSize, XpressLz77Mode mode)
    {
        // MS-XCA 2.4 Plain LZ77 Decompression: literal/match flags in 32-bit words, used from the top bit; 16-bit match token.
        // As in the specification and Windows, decoding runs until a match flag finds the input at its end.

        var expectedSize = output.Length;
        var src = 0;
        decompressedSize = 0;
        var cachedLenNibble = -1; // -1 = empty; otherwise 0..15

        while (true)
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
                var isMatch = (flags & 0x8000_0000u) != 0;
                flags <<= 1;

                if (!isMatch)
                {
                    // Literal
                    if (src >= compressed.Length || decompressedSize >= expectedSize)
                    {
                        return false;
                    }

                    output[decompressedSize++] = compressed[src++];

                    continue;
                }

                if (src == compressed.Length)
                {
                    // End-of-stream marker. Windows rejects a stream without data.
                    return mode == XpressLz77Mode.Strict || decompressedSize > 0;
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
                long matchLen;

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

                            // A 16-bit length minus 3, or if that is 0, a 32-bit one. Both are unsigned and hold the whole
                            // length, so [MS-XCA] 2.4 (and Windows) rejects values below 15 + 7, which the shorter forms cover.
                            uint extendedLength = BinaryPrimitives.ReadUInt16LittleEndian(compressed.Slice(src, 2));
                            src += 2;

                            if (extendedLength == 0)
                            {
                                if (src + 4 > compressed.Length)
                                {
                                    return false;
                                }

                                extendedLength = BinaryPrimitives.ReadUInt32LittleEndian(compressed.Slice(src, 4));
                                src += 4;
                            }

                            if (extendedLength < 15 + 7)
                            {
                                return false;
                            }

                            matchLen = extendedLength + 3L;
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
                if (matchLen > expectedSize - decompressedSize)
                {
                    return false;
                }

                var toCopy = (int)matchLen;

                var srcPos = decompressedSize - matchOffset;
                // Overlap-safe copy
                while (toCopy-- > 0)
                {
                    output[decompressedSize++] = output[srcPos++];
                }
            }
        }
    }

    /// <summary>
    /// Decompress XPRESS (Plain LZ77) stream into <paramref name="output"/>.
    /// </summary>
    /// <param name="compressed">Entire XPRESS block (no container header)</param>
    /// <param name="output">Buffer with decompressed size</param>
    /// <param name="mode">How to treat input on which Windows and [MS-XCA] disagree</param>
    /// <exception cref="InvalidDataException">On malformed input, or data that does not fill <paramref name="output"/></exception>
    public static void Decompress(ReadOnlySpan<byte> compressed, Span<byte> output, XpressLz77Mode mode = XpressLz77Mode.Windows)
    {
        if (!TryDecompress(compressed, output, out var decompressedSize, mode) || decompressedSize != output.Length)
        {
            throw new InvalidDataException("Malformed XPRESS compressed data");
        }
    }
}