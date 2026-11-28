using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace DiscUtils.Compression;

/// <summary>
/// XPRESS Huffman decompressor that operates directly on spans.
/// </summary>
/// <remarks>
/// How data outside the specification decodes is chosen by <see cref="XpressHuffmanMode"/>: <see cref="Default"/> and the static
/// <see cref="TryDecompress(ReadOnlySpan{byte}, Span{byte}, out int, out int)"/> use <see cref="XpressHuffmanMode.Strict"/>, and
/// <see cref="WindowsCompatible"/> uses <see cref="XpressHuffmanMode.Windows"/>.
/// </remarks>
public class XpressHuffman : IBlockDecompressor
{
    private const int SymbolCount = 512;
    private const int MaxCodeLength = 15;
    private const int FastBits = 10;
    private const int TableLength = SymbolCount / 2;
    private const int BlockLength = 65536;

    /// <summary>A shared instance that decodes in <see cref="XpressHuffmanMode.Strict"/> mode.</summary>
    public static XpressHuffman Default => field ??= new();

    /// <summary>A shared instance that decodes in <see cref="XpressHuffmanMode.Windows"/> mode, as <c>RtlDecompressBufferEx</c> does.</summary>
    public static XpressHuffman WindowsCompatible => field ??= new(XpressHuffmanMode.Windows);

    /// <summary>Creates an instance that decodes in <see cref="XpressHuffmanMode.Strict"/> mode.</summary>
    public XpressHuffman()
        : this(XpressHuffmanMode.Strict)
    {
    }

    /// <summary>Creates an instance that decodes in the given mode.</summary>
    public XpressHuffman(XpressHuffmanMode mode)
    {
        if (mode is not (XpressHuffmanMode.Strict or XpressHuffmanMode.Windows))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown XPRESS Huffman mode.");
        }

        Mode = mode;
    }

    /// <summary>How data outside the specification decodes.</summary>
    public XpressHuffmanMode Mode { get; }

    int IBlockDecompressor.BlockSize { get; set; }

    /// <summary>
    /// Decompresses XPRESS Huffman (LZ77+Huffman, [MS-XCA] 2.1 and 2.2) data into a caller-provided destination buffer, in
    /// <see cref="XpressHuffmanMode.Strict"/> mode.
    /// </summary>
    /// <inheritdoc cref="TryDecompress(ReadOnlySpan{byte}, Span{byte}, XpressHuffmanMode, out int, out int)"/>
    public static bool TryDecompress(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        out int bytesConsumed,
        out int bytesWritten)
        => TryDecompress(source, destination, XpressHuffmanMode.Strict, out bytesConsumed, out bytesWritten);

    /// <summary>
    /// Decompresses XPRESS Huffman (LZ77+Huffman, [MS-XCA] 2.1 and 2.2) data into a caller-provided destination buffer.
    /// </summary>
    /// <remarks>
    /// The output is made in blocks of 65536 bytes; each block's input starts with its own 256-byte table of code lengths. A match may run past
    /// the end of its block. The input is read through <see cref="XpressBitReader"/>, as in the [MS-XCA] 2.2.4 pseudocode.
    /// </remarks>
    /// <param name="source">Compressed data.</param>
    /// <param name="destination">Destination buffer. Its full length is treated as the expected uncompressed size.</param>
    /// <param name="mode">How data outside the specification decodes (see <see cref="XpressHuffmanMode"/>).</param>
    /// <param name="bytesConsumed">Number of source bytes consumed or read ahead by the decoder.</param>
    /// <param name="bytesWritten">Number of bytes written to destination.</param>
    /// <returns><see langword="true"/> on success, otherwise <see langword="false"/>.</returns>
    public static bool TryDecompress(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        XpressHuffmanMode mode,
        out int bytesConsumed,
        out int bytesWritten)
    {
        Span<byte> codeLengths = stackalloc byte[SymbolCount];
        Span<ushort> sortedSymbols = stackalloc ushort[SymbolCount];
        Span<ushort> lengthCounts = stackalloc ushort[MaxCodeLength + 1];
        Span<int> firstCode = stackalloc int[MaxCodeLength + 1];
        Span<int> firstSymbol = stackalloc int[MaxCodeLength + 1];
        Span<int> nextSymbol = stackalloc int[MaxCodeLength + 1];
        Span<ushort> fastSymbol = stackalloc ushort[1 << FastBits];
        Span<byte> fastLength = stackalloc byte[1 << FastBits];

        var reader = new XpressBitReader(source);
        var outPos = 0;
        var ok = false;

        while (true)
        {
            // [MS-XCA] 2.2.4 starts each block by checking for room for a table: without it, decoding succeeds if the output is
            // full and fails otherwise. Strict decoding stops as soon as the output is full. Windows goes on through the outer loop:
            // while there is room for a table (and its first two words), it reads and checks the table, and skips it.
            var outputFull = outPos >= destination.Length;
            if (outputFull && mode != XpressHuffmanMode.Windows)
            {
                break;
            }

            if (!reader.TryReadCodeLengths(codeLengths))
            {
                if (outputFull)
                {
                    break;
                }

                goto done;
            }

            if (!BuildDecoder(codeLengths, mode, sortedSymbols, lengthCounts, firstCode, firstSymbol, nextSymbol, fastSymbol, fastLength))
            {
                goto done;
            }

            var blockEnd = Math.Min(destination.Length, outPos + BlockLength);

            while (outPos < blockEnd)
            {
                var symbol = DecodeSymbol(reader.Peek15(), sortedSymbols, lengthCounts, firstCode, firstSymbol, fastSymbol, fastLength, out var symbolLength);
                if (symbol < 0)
                {
                    goto done;
                }

                if (!reader.TryConsume(symbolLength))
                {
                    goto done;
                }

                if (symbol < 256)
                {
                    destination[outPos++] = (byte)symbol;
                    continue;
                }

                symbol -= 256;
                long length = symbol & 0x0F;
                var offsetBits = symbol >> 4;

                // The extended length is read from the byte stream before the offset bits: a byte (the length minus 18), or 255 and a
                // 16-bit word (the length minus 3), or 255, a zero word and a 32-bit value (the length minus 3). The decoder pseudocode
                // of [MS-XCA] omits the 32-bit form, but its encoder writes it for lengths above 65538. Kept as a long, it cannot overflow.
                if (length == 15)
                {
                    if (!reader.TryReadRawByte(out var lengthByte))
                    {
                        goto done;
                    }

                    if (lengthByte == 255)
                    {
                        if (!reader.TryReadRawUInt16(out var lengthWord))
                        {
                            goto done;
                        }

                        length = lengthWord;
                        if (length == 0)
                        {
                            if (!reader.TryReadRawUInt32(out var longLength))
                            {
                                goto done;
                            }

                            length = longLength;
                        }

                        if (length < 15)
                        {
                            goto done;
                        }
                    }
                    else
                    {
                        length = lengthByte + 15;
                    }
                }

                length += 3;

                var offset = (int)reader.PeekBits(offsetBits) + (1 << offsetBits);
                if (!reader.TryConsume(offsetBits) || offset > outPos)
                {
                    goto done;
                }

                var remaining = destination.Length - outPos;
                if (length > remaining)
                {
                    // Windows fails a match that does not fit the output; strict decoding fills the output and stops.
                    if (mode == XpressHuffmanMode.Windows)
                    {
                        goto done;
                    }

                    length = remaining;
                }

                var from = outPos - offset;
                for (var i = 0; i < length; i++)
                {
                    destination[outPos++] = destination[from++];
                }
            }
        }

        ok = true;

    done:
        bytesConsumed = reader.BytesConsumed;
        bytesWritten = outPos;
        return ok;
    }

    /// <summary>The symbol whose code starts the 15 bits <paramref name="next15"/> (most significant bit first), or -1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeSymbol(
        uint next15,
        scoped ReadOnlySpan<ushort> sortedSymbols,
        scoped ReadOnlySpan<ushort> lengthCounts,
        scoped ReadOnlySpan<int> firstCode,
        scoped ReadOnlySpan<int> firstSymbol,
        scoped ReadOnlySpan<ushort> fastSymbol,
        scoped ReadOnlySpan<byte> fastLength,
        out int length)
    {
        var fast = (int)(next15 >> (MaxCodeLength - FastBits));
        length = fastLength[fast];
        if (length != 0)
        {
            return fastSymbol[fast];
        }

        for (length = FastBits + 1; length <= MaxCodeLength; length++)
        {
            var delta = (int)(next15 >> (MaxCodeLength - length)) - firstCode[length];
            if ((uint)delta < lengthCounts[length])
            {
                return sortedSymbols[firstSymbol[length] + delta];
            }
        }

        length = 0;
        return -1;
    }

    /// <summary>
    /// Builds the canonical decoder for a table of code lengths: false when the lengths are not a complete prefix code ([MS-XCA] 2.2.4
    /// requires the decoding table to be filled exactly). In <see cref="XpressHuffmanMode.Windows"/> mode, a single symbol with a 1-bit
    /// code is also accepted and decodes from either bit value, as Windows does.
    /// </summary>
    private static bool BuildDecoder(
        ReadOnlySpan<byte> codeLengths,
        XpressHuffmanMode mode,
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

        // Incomplete tree: some bit sequences would decode to no symbol.
        if (code != 1 << MaxCodeLength)
        {
            if (mode != XpressHuffmanMode.Windows || code != 1 << (MaxCodeLength - 1) || lengthCounts[1] != 1)
            {
                return false;
            }

            var single = codeLengths.IndexOf((byte)1);
            fastSymbol.Fill((ushort)single);
            fastLength.Fill(1);
            return true;
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

    bool IBlockDecompressor.TryDecompress(ReadOnlySpan<byte> source, Span<byte> decompressed, out int decompressedSize)
        => TryDecompress(source, decompressed, Mode, out _, out decompressedSize);

    /// <summary>
    /// The input of an XPRESS Huffman stream as [MS-XCA] 2.2.4 reads it: a byte position, and a bit buffer that always holds the next 32 bits
    /// (16 plus <c>_extraBits</c> of them valid) taken as little-endian 16-bit words, the first word in the more significant half. Raw bytes
    /// (extended match lengths, code-length tables) are read at the byte position, which is past the words already in the buffer. A word
    /// that the buffer needs but the input does not hold in full is an error, as in Windows; the encoder ends each stream with a zero word
    /// for this read-ahead.
    /// </summary>
    private ref struct XpressBitReader
    {
        private readonly ReadOnlySpan<byte> _source;
        private int _position;
        private uint _nextBits;
        private int _extraBits;

        public XpressBitReader(ReadOnlySpan<byte> source)
        {
            _source = source;
            _position = 0;
            _nextBits = 0;
            _extraBits = 0;
        }

        /// <summary>Bytes read from the input, including the words the bit buffer has read ahead.</summary>
        public readonly int BytesConsumed => _position;

        /// <summary>
        /// Reads a block's 256-byte table of 4-bit code lengths (the low half of a byte first) at the byte position, then fills the bit buffer
        /// with the next 32 bits. False when fewer than 260 bytes are left.
        /// </summary>
        public bool TryReadCodeLengths(scoped Span<byte> codeLengths)
        {
            if (_source.Length - _position < TableLength + 4)
            {
                return false;
            }

            for (var i = 0; i < TableLength; i++)
            {
                var b = _source[_position + i];
                codeLengths[2 * i] = (byte)(b & 0x0F);
                codeLengths[2 * i + 1] = (byte)(b >> 4);
            }

            _position += TableLength;
            _nextBits = ((uint)ReadWord(_position) << 16) | ReadWord(_position + 2);
            _position += 4;
            _extraBits = 16;
            return true;
        }

        /// <summary>The next 15 bits, most significant first (enough for the longest Huffman code).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly uint Peek15() => _nextBits >> (32 - MaxCodeLength);

        /// <summary>The next <paramref name="count"/> bits (0 to 16), most significant first.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly uint PeekBits(int count) => count == 0 ? 0 : _nextBits >> (32 - count);

        /// <summary>
        /// Drops <paramref name="count"/> bits (0 to 16) and reads the next word as soon as fewer than 16 spare bits remain. False when that
        /// word is not in the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryConsume(int count)
        {
            _nextBits <<= count;
            _extraBits -= count;
            if (_extraBits < 0)
            {
                if (_position + 2 > _source.Length)
                {
                    return false;
                }

                _nextBits |= (uint)ReadWord(_position) << -_extraBits;
                _extraBits += 16;
                _position += 2;
            }

            return true;
        }

        public bool TryReadRawByte(out byte value)
        {
            if (_position >= _source.Length)
            {
                value = 0;
                return false;
            }

            value = _source[_position++];
            return true;
        }

        public bool TryReadRawUInt16(out ushort value)
        {
            if (_position + 2 > _source.Length)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadUInt16LittleEndian(_source.Slice(_position, 2));
            _position += 2;
            return true;
        }

        public bool TryReadRawUInt32(out uint value)
        {
            if (_position + 4 > _source.Length)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadUInt32LittleEndian(_source.Slice(_position, 4));
            _position += 4;
            return true;
        }

        /// <summary>A little-endian word at a position the caller has checked.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly ushort ReadWord(int position) => BinaryPrimitives.ReadUInt16LittleEndian(_source.Slice(position, 2));
    }
}
