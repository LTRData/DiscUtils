using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace DiscUtils.Compression;

/// <summary>
/// XPRESS Huffman decompressor that operates directly on spans.
/// </summary>
public class XpressHuffman : IBlockDecompressor
{
    private const int SymbolCount = 512;
    private const int MaxCodeLength = 15;
    private const int FastBits = 10;

    public static XpressHuffman Default => field ??= new();

    int IBlockDecompressor.BlockSize { get; set; }

    /// <summary>
    /// Decompresses an XPRESS Huffman block into a caller-provided destination buffer.
    /// </summary>
    /// <param name="source">Compressed source block.</param>
    /// <param name="destination">Destination buffer. Its full length is treated as the expected uncompressed size.</param>
    /// <param name="bytesConsumed">Number of source bytes consumed or read-ahead by the decoder.</param>
    /// <param name="bytesWritten">Number of bytes written to destination.</param>
    /// <returns><see langword="true"/> on success, otherwise <see langword="false"/>.</returns>
    public static bool TryDecompress(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        out int bytesConsumed,
        out int bytesWritten)
    {
        bytesConsumed = 0;
        bytesWritten = 0;

        if (source.Length < 256)
        {
            return false;
        }

        Span<byte> codeLengths = stackalloc byte[SymbolCount];
        Span<ushort> sortedSymbols = stackalloc ushort[SymbolCount];
        Span<ushort> lengthCounts = stackalloc ushort[MaxCodeLength + 1];
        Span<int> firstCode = stackalloc int[MaxCodeLength + 1];
        Span<int> firstSymbol = stackalloc int[MaxCodeLength + 1];
        Span<int> nextSymbol = stackalloc int[MaxCodeLength + 1];
        Span<ushort> fastSymbol = stackalloc ushort[1 << FastBits];
        Span<byte> fastLength = stackalloc byte[1 << FastBits];

        int srcPos = 0;

        // First 256 bytes contain two 4-bit code lengths each.
        for (int i = 0; i < SymbolCount; i += 2)
        {
            byte b = source[srcPos++];
            codeLengths[i] = (byte)(b & 0x0F);
            codeLengths[i + 1] = (byte)(b >> 4);
        }

        if (!BuildDecoder(
            codeLengths,
            sortedSymbols,
            lengthCounts,
            firstCode,
            firstSymbol,
            nextSymbol,
            fastSymbol,
            fastLength))
        {
            return false;
        }

        var reader = new XpressBitReader(source, srcPos);
        int dstPos = 0;

        while (dstPos < destination.Length)
        {
            int symbol = DecodeSymbol(
                ref reader,
                sortedSymbols,
                lengthCounts,
                firstCode,
                firstSymbol,
                fastSymbol,
                fastLength);

            if (symbol < 0)
            {
                bytesConsumed = reader.BytesConsumed;
                bytesWritten = dstPos;
                return false;
            }

            if (symbol < 256)
            {
                destination[dstPos++] = (byte)symbol;
                continue;
            }

            int slot = symbol - 256;
            int offsetBits = slot >> 4;
            int len = slot & 0x0F;

            if (!reader.TryReadBits(offsetBits, out uint extraBits))
            {
                bytesConsumed = reader.BytesConsumed;
                bytesWritten = dstPos;
                return false;
            }

            int offset = ((1 << offsetBits) - 1) + (int)extraBits;

            if (len == 15)
            {
                if (!reader.TryReadRawByte(out byte b))
                {
                    bytesConsumed = reader.BytesConsumed;
                    bytesWritten = dstPos;
                    return false;
                }

                if (b == 0xFF)
                {
                    if (!reader.TryReadRawUInt16(out ushort extraLen))
                    {
                        bytesConsumed = reader.BytesConsumed;
                        bytesWritten = dstPos;
                        return false;
                    }

                    len = extraLen;
                }
                else
                {
                    len += b;
                }
            }

            len += 3;

            if (!CopyMatch(destination, ref dstPos, offset + 1, len))
            {
                bytesConsumed = reader.BytesConsumed;
                bytesWritten = dstPos;
                return false;
            }
        }

        bytesConsumed = reader.BytesConsumed;
        bytesWritten = dstPos;
        return true;
    }

    private static bool BuildDecoder(
        ReadOnlySpan<byte> codeLengths,
        Span<ushort> sortedSymbols,
        Span<ushort> lengthCounts,
        Span<int> firstCode,
        Span<int> firstSymbol,
        Span<int> nextSymbol,
        Span<ushort> fastSymbol,
        Span<byte> fastLength)
    {
        lengthCounts.Clear();
        firstCode.Clear();
        firstSymbol.Clear();
        nextSymbol.Clear();
        fastSymbol.Clear();
        fastLength.Clear();

        for (int symbol = 0; symbol < codeLengths.Length; symbol++)
        {
            int len = codeLengths[symbol];
            if ((uint)len > MaxCodeLength)
            {
                return false;
            }

            if (len != 0)
            {
                lengthCounts[len]++;
            }
        }

        int runningSymbolIndex = 0;
        int code = 0;

        for (int len = 1; len <= MaxCodeLength; len++)
        {
            code <<= 1;
            firstCode[len] = code;
            firstSymbol[len] = runningSymbolIndex;
            nextSymbol[len] = runningSymbolIndex;

            runningSymbolIndex += lengthCounts[len];
            code += lengthCounts[len];

            // Oversubscribed tree.
            if (code > (1 << len))
            {
                return false;
            }
        }

        for (int symbol = 0; symbol < codeLengths.Length; symbol++)
        {
            int len = codeLengths[symbol];
            if (len == 0)
            {
                continue;
            }

            sortedSymbols[nextSymbol[len]++] = (ushort)symbol;
        }

        for (int len = 1; len <= FastBits; len++)
        {
            int count = lengthCounts[len];
            if (count == 0)
            {
                continue;
            }

            int baseCode = firstCode[len];
            int symbolBase = firstSymbol[len];
            int fill = 1 << (FastBits - len);

            for (int i = 0; i < count; i++)
            {
                int codeValue = baseCode + i;
                int start = codeValue << (FastBits - len);
                ushort symbol = sortedSymbols[symbolBase + i];

                for (int j = 0; j < fill; j++)
                {
                    fastSymbol[start + j] = symbol;
                    fastLength[start + j] = (byte)len;
                }
            }
        }

        return true;
    }

#if true
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeSymbol(
    scoped ref XpressBitReader reader,
    scoped ReadOnlySpan<ushort> sortedSymbols,
    scoped ReadOnlySpan<ushort> lengthCounts,
    scoped ReadOnlySpan<int> firstCode,
    scoped ReadOnlySpan<int> firstSymbol,
    scoped ReadOnlySpan<ushort> fastSymbol,
    scoped ReadOnlySpan<byte> fastLength)
    {
        if (!reader.TryPeekBits(1, out _))
        {
            return -1;
        }

        if (reader.TryPeekBits(FastBits, out uint fastPeek))
        {
            byte len = fastLength[(int)fastPeek];
            if (len != 0)
            {
                if (!reader.TryConsumeBits(len))
                {
                    return -1;
                }

                return fastSymbol[(int)fastPeek];
            }
        }

        for (int len = 1; len <= MaxCodeLength; len++)
        {
            if (!reader.TryPeekBits(len, out uint peek))
            {
                return -1;
            }

            int code = (int)peek;
            int delta = code - firstCode[len];
            int count = lengthCounts[len];

            if ((uint)delta < (uint)count)
            {
                if (!reader.TryConsumeBits(len))
                {
                    return -1;
                }

                return sortedSymbols[firstSymbol[len] + delta];
            }
        }

        return -1;
    }
#else
    private static int DecodeSymbol(
        scoped ref XpressBitReader reader,
        scoped ReadOnlySpan<ushort> sortedSymbols,
        scoped ReadOnlySpan<ushort> lengthCounts,
        scoped ReadOnlySpan<int> firstCode,
        scoped ReadOnlySpan<int> firstSymbol,
        scoped ReadOnlySpan<ushort> fastSymbol,
        scoped ReadOnlySpan<byte> fastLength)
    {
        if (!reader.TryPeekBits(1, out _))
        {
            return -1;
        }

        for (int len = 1; len <= MaxCodeLength; len++)
        {
            if (!reader.TryPeekBits(len, out uint peek))
            {
                return -1;
            }

            int code = (int)peek;
            int delta = code - firstCode[len];
            int count = lengthCounts[len];

            if ((uint)delta < (uint)count)
            {
                if (!reader.TryConsumeBits(len))
                {
                    return -1;
                }

                return sortedSymbols[firstSymbol[len] + delta];
            }
        }

        return -1;
    }
#endif

    private static bool CopyMatch(Span<byte> destination, ref int dstPos, int distance, int length)
    {
        if (distance <= 0 || distance > dstPos)
        {
            return false;
        }

        int remaining = destination.Length - dstPos;
        if (remaining < 0)
        {
            return false;
        }

        if (length > remaining)
        {
            length = remaining;
        }

        int srcPos = dstPos - distance;

        for (int i = 0; i < length; i++)
        {
            destination[dstPos++] = destination[srcPos++];
        }

        return true;
    }

    bool IBlockDecompressor.TryDecompress(ReadOnlySpan<byte> source, Span<byte> decompressed, out int decompressedSize)
        => TryDecompress(source, decompressed, out _, out decompressedSize);

    private ref struct XpressBitReader
    {
        private readonly ReadOnlySpan<byte> _source;
        private int _rawPos;
        private uint _bitBuffer;
        private int _bitsAvailable;

        public XpressBitReader(ReadOnlySpan<byte> source, int start)
        {
            _source = source;
            _rawPos = start;
            _bitBuffer = 0;
            _bitsAvailable = 0;
        }

        public readonly int BytesConsumed => _rawPos;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool EnsureBufferFilled()
        {
            // Match old XpressBitStream exactly:
            // refill whenever fewer than 16 bits remain.
            if (_bitsAvailable < 16)
            {
                if ((uint)(_rawPos + 1) >= (uint)_source.Length)
                {
                    return false;
                }

                ushort word = BinaryPrimitives.ReadUInt16LittleEndian(_source.Slice(_rawPos, 2));
                _rawPos += 2;
                _bitBuffer = (_bitBuffer << 16) | word;
                _bitsAvailable += 16;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanReadBits(int count)
        {
            if ((uint)count > 16u)
            {
                return false;
            }

            return EnsureBufferFilled() && _bitsAvailable >= count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint PeekBits(int count)
        {
            uint mask = (1u << count) - 1u;
            return (_bitBuffer >> (_bitsAvailable - count)) & mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadBits(int count)
        {
            if (!EnsureBufferFilled())
            {
                return 0;
            }

            _bitsAvailable -= count;
            uint mask = (1u << count) - 1u;
            return (_bitBuffer >> _bitsAvailable) & mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadBits(int count, out uint value)
        {
            value = 0;

            if ((uint)count > 16u)
            {
                return false;
            }

            if (!EnsureBufferFilled() || _bitsAvailable < count)
            {
                return false;
            }

            _bitsAvailable -= count;

            if (count == 0)
            {
                value = 0;
                return true;
            }

            uint mask = (1u << count) - 1u;
            value = (_bitBuffer >> _bitsAvailable) & mask;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPeekBits(int count, out uint value)
        {
            value = 0;

            if ((uint)count > 16u)
            {
                return false;
            }

            // Also important: Peek(0) in the old implementation still caused refill.
            if (!EnsureBufferFilled())
            {
                return false;
            }

            if (_bitsAvailable < count)
            {
                return false;
            }

            if (count == 0)
            {
                value = 0;
                return true;
            }

            uint mask = (1u << count) - 1u;
            value = (_bitBuffer >> (_bitsAvailable - count)) & mask;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryConsumeBits(int count)
        {
            if ((uint)count > 16u)
            {
                return false;
            }

            if (!EnsureBufferFilled())
            {
                return false;
            }

            if (_bitsAvailable < count)
            {
                return false;
            }

            _bitsAvailable -= count;
            return true;
        }

        // These intentionally read from the raw stream position,
        // matching the old decoder.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadRawByte(out byte value)
        {
            if ((uint)_rawPos >= (uint)_source.Length)
            {
                value = 0;
                return false;
            }

            value = _source[_rawPos++];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadRawUInt16(out ushort value)
        {
            if ((uint)(_rawPos + 1) >= (uint)_source.Length)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadUInt16LittleEndian(_source.Slice(_rawPos, 2));
            _rawPos += 2;
            return true;
        }
    }
}
