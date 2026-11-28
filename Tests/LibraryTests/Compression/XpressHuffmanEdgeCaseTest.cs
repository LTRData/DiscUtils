using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using DiscUtils.Compression;
using DiscUtils.Streams;
using static LibraryTests.Compression.NativeCompression;

namespace LibraryTests.Compression;

/// <summary>
/// Edge cases of the XPRESS Huffman (LZ77+Huffman) format ([MS-XCA] 2.1 and 2.2) built by hand: code-length tables, the 16-bit
/// word bit stream with raw extended-length bytes interleaved at the read-ahead position, match lengths and offsets at every
/// encoding boundary, 64 KiB blocks, malformed input and short output buffers. The vectors are written with the encoder of
/// [MS-XCA] 2.1.4.3 and canonical codes assigned as in the 2.2.4 table method, and their expected output comes from the plain
/// LZ77 meaning of the tokens. Both <see cref="XpressHuffmanMode"/>s are checked; where they differ, the test says how. On Windows,
/// <see cref="XpressHuffmanMode.Windows"/> is also compared with <c>RtlDecompressBufferEx</c>.
/// </summary>
public class XpressHuffmanEdgeCaseTest
{
#if NET471_OR_GREATER || NETCOREAPP
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
    private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
#endif

    private const int BlockSize = 65536;

    #region Stream builder

    /// <summary>A literal byte or a back reference.</summary>
    private readonly record struct Token(byte Literal, int Offset, int Length)
    {
        public bool IsMatch => Length > 0;
    }

    private static Token Lit(byte b) => new(b, 0, 0);

    private static Token Match(int offset, int length) => new(0, offset, length);

    private static IEnumerable<Token> Lits(IEnumerable<byte> bytes) => bytes.Select(Lit);

    private static int HighBit(int value)
    {
        var bit = 0;
        while ((value >> (bit + 1)) != 0)
        {
            bit++;
        }

        return bit;
    }

    /// <summary>The Huffman symbol of a match ([MS-XCA] 2.1.4.1): the length minus 3 (at most 15) plus 16 times the offset's high bit.</summary>
    private static int MatchSymbol(int offset, int length) => 256 + Math.Min(length - 3, 15) + 16 * HighBit(offset);

    /// <summary>A table giving every one of the 512 symbols a 9-bit code: complete, and able to encode any literal or match.</summary>
    private static byte[] FlatLengths() => Enumerable.Repeat((byte)9, 512).ToArray();

    private static byte[] LengthsOf(params (int Symbol, int Length)[] codes)
    {
        var lengths = new byte[512];
        foreach (var (symbol, length) in codes)
        {
            lengths[symbol] = (byte)length;
        }

        return lengths;
    }

    /// <summary>
    /// Writes one block the way the [MS-XCA] 2.1.4.3 encoder does: the 256-byte table of 4-bit code lengths, then a stream of
    /// 16-bit little-endian words (most significant bit first) with two word slots reserved ahead, so that the raw bytes of an
    /// extended match length land where a decoder holding 32 bits of read-ahead reads them next.
    /// </summary>
    private sealed class BlockWriter
    {
        private readonly List<byte> _output = [];
        private readonly byte[] _lengths;
        private readonly uint[] _codes = new uint[512];
        private int _freeBits = 16;
        private uint _nextWord;
        private int _position1 = 256;
        private int _position2 = 258;
        private int _position = 260;

        public BlockWriter(byte[] lengths)
        {
            Assert.Equal(512, lengths.Length);
            _lengths = lengths;

            for (var i = 0; i < 256; i++)
            {
                _output.Add((byte)((lengths[2 * i] & 0x0F) | (lengths[2 * i + 1] << 4)));
            }

            // Canonical codes as the [MS-XCA] 2.2.4 decoding table lays them out: by length, then by symbol value. A symbol's code
            // is the index of its first table entry, shifted down to its length. A table that is not a valid prefix code is still
            // written (to make malformed vectors); its codes are then only meaningful up to the point where the table overflows.
            var entry = 0L;
            for (var length = 1; length <= 15; length++)
            {
                for (var symbol = 0; symbol < 512; symbol++)
                {
                    if (lengths[symbol] == length)
                    {
                        _codes[symbol] = (uint)((entry >> (15 - length)) & ((1 << length) - 1));
                        entry += 1L << (15 - length);
                    }
                }
            }
        }

        public void Bits(int count, uint bits)
        {
            Assert.InRange(count, 0, 16);
            if (_freeBits >= count)
            {
                _freeBits -= count;
                _nextWord = (_nextWord << count) + bits;
            }
            else
            {
                _nextWord <<= _freeBits;
                _nextWord += bits >> (count - _freeBits);
                _freeBits -= count;
                WriteWord(_position1, (ushort)_nextWord);
                _position1 = _position2;
                _position2 = _position;
                _position += 2;
                _freeBits += 16;
                _nextWord = bits;
            }
        }

        public void Symbol(int symbol)
        {
            Assert.True(_lengths[symbol] != 0, $"symbol {symbol} has no code in this table");
            Bits(_lengths[symbol], _codes[symbol]);
        }

        public void Byte(byte value) => Raw([value]);

        public void UInt16(ushort value) => Raw([(byte)value, (byte)(value >> 8)]);

        public void UInt32(uint value) => Raw([(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)]);

        public void Token(Token token)
        {
            if (!token.IsMatch)
            {
                Symbol(token.Literal);
                return;
            }

            Assert.InRange(token.Offset, 1, 65535);
            Assert.True(token.Length >= 3);
            Symbol(MatchSymbol(token.Offset, token.Length));
            var length = token.Length - 3;
            if (length >= 15)
            {
                length -= 15;
                if (length < 255)
                {
                    Byte((byte)length);
                }
                else
                {
                    Byte(255);
                    length += 15;
                    if (length < 65536)
                    {
                        UInt16((ushort)length);
                    }
                    else
                    {
                        UInt16(0);
                        UInt32((uint)length);
                    }
                }
            }

            OffsetBits(token.Offset);
        }

        public void OffsetBits(int offset)
        {
            var highBit = HighBit(offset);
            Bits(highBit, (uint)(offset - (1 << highBit)));
        }

        public void Tokens(IEnumerable<Token> tokens)
        {
            foreach (var token in tokens)
            {
                Token(token);
            }
        }

        /// <summary>Writes the end-of-data symbol 256 (when asked and the table has a code for it) and flushes the last words.</summary>
        public byte[] Finish(bool endSymbol = true)
        {
            if (endSymbol && _lengths[256] != 0)
            {
                Symbol(256);
            }

            _nextWord <<= _freeBits;
            WriteWord(_position1, (ushort)_nextWord);
            WriteWord(_position2, 0);
            EnsureLength(_position);
            return _output.Take(_position).ToArray();
        }

        private void Raw(byte[] bytes)
        {
            EnsureLength(_position + bytes.Length);
            for (var i = 0; i < bytes.Length; i++)
            {
                _output[_position + i] = bytes[i];
            }

            _position += bytes.Length;
        }

        private void WriteWord(int position, ushort value)
        {
            EnsureLength(position + 2);
            _output[position] = (byte)value;
            _output[position + 1] = (byte)(value >> 8);
        }

        private void EnsureLength(int length)
        {
            while (_output.Count < length)
            {
                _output.Add(0);
            }
        }
    }

    /// <summary>
    /// Tokens in as many flat-table blocks as the decoder expects: a block covers the 65536 bytes from where it starts, and the
    /// next block starts after the token that reaches or passes that point (a match may run past it).
    /// </summary>
    private static byte[] Encode(IEnumerable<Token> tokens, bool endSymbol = true)
    {
        var blocks = new List<byte[]>();
        var writer = new BlockWriter(FlatLengths());
        var position = 0L;
        var blockEnd = (long)BlockSize;
        foreach (var token in tokens)
        {
            if (position >= blockEnd)
            {
                blocks.Add(writer.Finish(endSymbol: false));
                writer = new BlockWriter(FlatLengths());
                blockEnd = position + BlockSize;
            }

            writer.Token(token);
            position += token.IsMatch ? token.Length : 1;
        }

        blocks.Add(writer.Finish(endSymbol));
        return Concat([.. blocks]);
    }

    private static byte[] Block(byte[] lengths, IEnumerable<Token> tokens, bool endSymbol = true)
    {
        var writer = new BlockWriter(lengths);
        writer.Tokens(tokens);
        return writer.Finish(endSymbol);
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>The plain LZ77 meaning of the tokens: literals, and byte-by-byte copies that may overlap their own output.</summary>
    private static byte[] Expand(IEnumerable<Token> tokens)
    {
        var output = new List<byte>();
        foreach (var token in tokens)
        {
            if (!token.IsMatch)
            {
                output.Add(token.Literal);
                continue;
            }

            var from = output.Count - token.Offset;
            for (var i = 0; i < token.Length; i++)
            {
                output.Add(output[from + i]);
            }
        }

        return output.ToArray();
    }

    private static byte[] Distinct(int count) => Enumerable.Range(0, count).Select(i => (byte)(i * 7 % 251 + 1)).ToArray();

    #endregion

    #region Helpers

    private static (bool Ok, byte[] Output, int Consumed) Decompress(byte[] compressed, int outputSize, XpressHuffmanMode mode = XpressHuffmanMode.Strict)
    {
        var output = new byte[outputSize];
        var ok = XpressHuffman.TryDecompress(compressed, output, mode, out var consumed, out var written);
        Assert.InRange(consumed, 0, compressed.Length);
        Assert.InRange(written, 0, outputSize);
        return (ok, output.AsSpan(0, written).ToArray(), consumed);
    }

    /// <summary>Decodes in both modes and expects <paramref name="expected"/>, without comparing with Windows.</summary>
    private static void AssertDecoded(byte[] compressed, int outputSize, byte[] expected)
    {
        foreach (var mode in new[] { XpressHuffmanMode.Strict, XpressHuffmanMode.Windows })
        {
            var (ok, output, _) = Decompress(compressed, outputSize, mode);
            Assert.True(ok, $"{mode}: decoding failed");
            Assert.Equal(expected, output);
        }
    }

    /// <summary>Decodes in both modes and expects <paramref name="expected"/>; on Windows, also expects the same result from Windows.</summary>
    private static void AssertDecompresses(byte[] compressed, int outputSize, byte[] expected)
    {
        AssertDecoded(compressed, outputSize, expected);
        AssertMatchesWindows(compressed, outputSize);
    }

    /// <summary>Expects both modes to reject the data; on Windows, also expects Windows to reject it.</summary>
    private static void AssertRejected(byte[] compressed, int outputSize)
    {
        AssertModes(compressed, outputSize, strict: null, windows: null);
    }

    /// <summary>
    /// For data on which the modes differ: expects <paramref name="strict"/> from <see cref="XpressHuffmanMode.Strict"/> and
    /// <paramref name="windows"/> from <see cref="XpressHuffmanMode.Windows"/> (null: rejected), and on Windows the same result as
    /// Windows from the latter.
    /// </summary>
    private static void AssertModes(byte[] compressed, int outputSize, byte[]? strict, byte[]? windows)
    {
        AssertMode(compressed, outputSize, XpressHuffmanMode.Strict, strict);
        AssertMode(compressed, outputSize, XpressHuffmanMode.Windows, windows);
        AssertMatchesWindows(compressed, outputSize);
    }

    private static void AssertMode(byte[] compressed, int outputSize, XpressHuffmanMode mode, byte[]? expected)
    {
        var (ok, output, _) = Decompress(compressed, outputSize, mode);
        if (expected is null)
        {
            Assert.False(ok, $"{mode}: malformed data was accepted");
        }
        else
        {
            Assert.True(ok, $"{mode}: decoding failed");
            Assert.Equal(expected, output);
        }
    }

    /// <summary>
    /// On Windows, expects <see cref="XpressHuffmanMode.Windows"/> to succeed or fail exactly when <c>RtlDecompressBufferEx</c> does,
    /// with the same output.
    /// </summary>
    private static void AssertMatchesWindows(byte[] compressed, int outputSize)
    {
        if (!IsWindows)
        {
            return;
        }

        var status = NativeDecompressStatus(compressed, outputSize, CompressionFormat.XpressHuff, out var native);
        var (ok, output, _) = Decompress(compressed, outputSize, XpressHuffmanMode.Windows);
        Assert.True(status == 0 == ok, $"Windows returned 0x{status:X8}, Windows mode returned {ok}");
        if (ok)
        {
            Assert.Equal(native, output);
        }
    }

    #endregion

    #region Windows oracle

    [WindowsOnlyFact]
    public void WindowsDecompressesItsOwnOutput()
    {
        // A positive control for the comparisons below: they would pass trivially if RtlDecompressBuffer rejected everything.
        var data = Sample("mixed", 150_000);
        var compressed = NativeCompress(data, 0, data.Length, 4096, CompressionFormat.XpressHuff);
        Assert.Equal(0, NativeDecompressStatus(compressed, data.Length, CompressionFormat.XpressHuff, out var native));
        Assert.Equal(data, native);
    }

    [WindowsOnlyFact]
    public void WindowsDecodesTheVectorBuilder()
    {
        Token[] tokens = [.. Lits("abcdefgh"u8.ToArray()), Match(8, 30), Match(1, 300), Lit((byte)'z')];
        var compressed = Encode(tokens);
        Assert.Equal(0, NativeDecompressStatus(compressed, Expand(tokens).Length, CompressionFormat.XpressHuff, out var native));
        Assert.Equal(Expand(tokens), native);
    }

    #endregion

    #region Code-length tables

    [Fact]
    public void DecodesAFlatTableOfNineBitCodes()
    {
        var tokens = Lits(Enumerable.Range(0, 256).Select(i => (byte)i)).ToList();
        tokens.Add(Match(256, 18));
        AssertDecompresses(Encode(tokens), tokens.Count - 1 + 18, Expand(tokens));
    }

    /// <summary>
    /// A complete code with one symbol at each length from 1 to 14 and two at 15, so that codes on both sides of the decoder's
    /// fast-lookup width (10 bits) and the longest codes are all used. The symbols mix literals, symbol 256 and a long match symbol.
    /// </summary>
    private static readonly (int Symbol, int Length)[] SkewedCodes =
    [
        ('a', 1), ('b', 2), (256, 3), ('c', 4), ('d', 5), ('e', 6), ('f', 7), ('g', 8), ('h', 9), ('i', 10),
        ('j', 11), (256 + 15 + 16 * 2, 12), ('k', 13), ('l', 14), ('m', 15), (256 + 2 + 16 * 15, 15),
    ];

    [Fact]
    public void DecodesEveryCodeLengthFromOneToFifteen()
    {
        var lengths = LengthsOf(SkewedCodes);
        var tokens = new List<Token>();
        foreach (var c in "mlkjihgfedcba")
        {
            tokens.Add(Lit((byte)c));
        }

        tokens.Add(Match(1, 3));          // symbol 256, 3 bits
        tokens.Add(Match(4, 100));        // symbol 256+15+32, 12 bits, extended length
        tokens.AddRange(Lits("mmllkkjjii"u8.ToArray()));
        tokens.AddRange(Lits(Enumerable.Repeat((byte)'a', 40_000)));
        tokens.Add(Match(32768, 5));      // symbol 256+2+240, 15 bits, 15 offset bits
        tokens.AddRange(Lits("abm"u8.ToArray()));

        var expected = Expand(tokens);
        AssertDecompresses(Block(lengths, tokens), expected.Length, expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(15)]
    public void DecodesTwoSymbolsOfEqualLengthPaddedToAFullCode(int length)
    {
        // Two literals with codes of the given length, plus filler symbols that complete the code without being used.
        var codes = new List<(int, int)> { ('x', length), ('y', length) };
        for (var l = length - 1; l >= 1; l--)
        {
            codes.Add((300 + l, l));
        }

        Token[] tokens = [.. Lits("xyyxxxyx"u8.ToArray())];
        AssertDecompresses(Block(LengthsOf([.. codes]), tokens, endSymbol: false), 8, "xyyxxxyx"u8.ToArray());
    }

    [Fact]
    public void RejectsAnOversubscribedTable()
    {
        // Three 1-bit codes.
        var lengths = LengthsOf(('a', 1), ('b', 1), ('c', 1));
        var compressed = Concat(new BlockWriter(lengths).Finish(endSymbol: false), new byte[16]);
        AssertRejected(compressed, 4);
    }

    [Fact]
    public void RejectsAnOversubscribedTableWithLongCodes()
    {
        // A complete code (every symbol 9 bits) plus one more 15-bit code.
        var lengths = FlatLengths();
        lengths[0] = 8;
        lengths[1] = 8;
        lengths[2] = 15;
        var compressed = Concat(Block(lengths, Lits("\u0003\u0004"u8.ToArray()), endSymbol: false), new byte[16]);
        AssertRejected(compressed, 2);
    }

    [Fact]
    public void RejectsAnAllZeroTable()
    {
        AssertRejected(new byte[256 + 16], 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    [InlineData(259)]
    public void AnEmptyOutputNeedsNoTable(int inputLength)
    {
        AssertDecompresses(new byte[inputLength], 0, []);
    }

    [Theory]
    [InlineData(260)]
    [InlineData(256 + 16)]
    public void AnEmptyOutputChecksTheTableOnlyInWindowsMode(int inputLength)
    {
        // With room for a table and its first two words, Windows reads the table even though there is nothing to write, and fails
        // on this all-zero one. Strict decoding does not look at the input.
        AssertModes(new byte[inputLength], 0, strict: [], windows: null);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(15)]
    public void RejectsASingleSymbolWithACodeLongerThanOneBit(int length)
    {
        AssertRejected(Block(LengthsOf(('a', length)), Lits("aaaa"u8.ToArray()), endSymbol: false), 4);
    }

    [Fact]
    public void ASingleOneBitSymbolIsAcceptedOnlyInWindowsMode()
    {
        // [MS-XCA] 2.2.4 rejects any table that does not fill the decoding table, and a lone 1-bit code fills half of it. Windows
        // accepts this one incomplete table, and decodes the symbol from either bit value.
        var lengths = LengthsOf(('a', 1));
        AssertModes(Block(lengths, Lits("aaaa"u8.ToArray()), endSymbol: false), 4, strict: null, windows: "aaaa"u8.ToArray());

        var writer = new BlockWriter(lengths);
        writer.Bits(1, 0);
        writer.Bits(1, 1);
        writer.Bits(1, 1);
        AssertModes(writer.Finish(endSymbol: false), 3, strict: null, windows: "aaa"u8.ToArray());
    }

    [Fact]
    public void ASingleOneBitMatchSymbolIsStillAMatch()
    {
        // Symbol 256 alone is a match at output position 0, which neither mode can decode.
        AssertRejected(new BlockWriter(LengthsOf((256, 1))).Finish(endSymbol: false), 4);
    }

    [Fact]
    public void RejectsAnIncompleteTable()
    {
        // Codes 0 and 10 are assigned, 11 is not: the table is rejected before any code is read.
        var lengths = LengthsOf(('a', 1), ('b', 2));
        AssertRejected(Block(lengths, Lits("abab"u8.ToArray()), endSymbol: false), 4);

        var writer = new BlockWriter(lengths);
        writer.Symbol('a');
        writer.Bits(2, 3);
        AssertRejected(writer.Finish(endSymbol: false), 2);
    }

    [Fact]
    public void RejectsATableOneLongCodeShortOfComplete()
    {
        var lengths = FlatLengths();
        lengths[500] = 0;
        AssertRejected(Block(lengths, Lits("hello"u8.ToArray()), endSymbol: false), 5);

        lengths = LengthsOf(('a', 15), ('b', 15));
        AssertRejected(Block(lengths, Lits("abba"u8.ToArray()), endSymbol: false), 4);
    }

    #endregion

    #region Symbol 256

    [Fact]
    public void SymbolTwoFiftySixMidStreamIsAMatch()
    {
        // Symbol 256 is also the match of length 3 at offset 1; only after the last byte is it an end marker.
        Token[] tokens = [Lit((byte)'q'), Match(1, 3), Lit((byte)'r'), Match(1, 3), Lit((byte)'s')];
        AssertDecompresses(Encode(tokens), 9, "qqqqrrrrs"u8.ToArray());
    }

    [Fact]
    public void RejectsSymbolTwoFiftySixAtTheStart()
    {
        // At output position 0 the symbol is a match with no data behind it.
        AssertRejected(Encode([Match(1, 3)]), 3);
    }

    [Fact]
    public void TheEndSymbolIsOptional()
    {
        Token[] tokens = [.. Lits("hello"u8.ToArray()), Match(5, 10)];
        AssertDecompresses(Encode(tokens, endSymbol: false), 15, Expand(tokens));
        AssertDecompresses(Encode(tokens, endSymbol: true), 15, Expand(tokens));
    }

    [Fact]
    public void TheEndSymbolDoesNotEndTheDataEarly()
    {
        // With an output larger than the data, the end symbol decodes as a match (offset 1, length 3) and the decoder then runs
        // out of input: both modes fail, as Windows does, instead of returning the data padded with zeros.
        Token[] tokens = [.. Lits("hello"u8.ToArray()), Match(5, 10)];
        AssertRejected(Encode(tokens, endSymbol: true), 20);
        AssertRejected(Encode(tokens, endSymbol: false), 20);
        AssertRejected(Encode(Lits("abc"u8.ToArray()), endSymbol: true), 10);
    }

    [Fact]
    public void IgnoresSymbolsAfterTheOutputIsFull()
    {
        // Symbols past the expected size, even a match reaching before the start of the data, are not decoded.
        var writer = new BlockWriter(FlatLengths());
        writer.Tokens(Lits("abc"u8.ToArray()));
        writer.Token(Match(1000, 3));
        AssertDecompresses(writer.Finish(), 3, "abc"u8.ToArray());
    }

    #endregion

    #region Match lengths

    public static TheoryData<int> MatchLengths => new() { 3, 4, 17, 18, 19, 272, 273, 274, 1000, 65535, 65538, 65539, 70_000 };

    [Theory]
    [MemberData(nameof(MatchLengths))]
    public void DecodesMatchLengthsAtEachEncodingBoundary(int length)
    {
        // 3..17 in the symbol, 18..272 in an extra byte, up to 65538 in a 16-bit word, then a 32-bit value. A literal after the
        // match shows that its raw bytes were consumed exactly.
        Token[] tokens = [.. Lits("0123456789"u8.ToArray()), Match(7, length), Lit((byte)'!')];
        var expected = Expand(tokens);
        AssertDecompresses(Encode(tokens), expected.Length, expected);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(269)]
    [InlineData(270)]
    public void AcceptsARedundantSixteenBitLength(int word)
    {
        // The 16-bit form of a length the byte form could hold (word 15 is length 18): the encoder never writes it, but the
        // decoding rule allows it.
        var writer = new BlockWriter(FlatLengths());
        writer.Tokens(Lits("ab"u8.ToArray()));
        writer.Symbol(256 + 15 + 16 * 1);
        writer.Byte(255);
        writer.UInt16((ushort)word);
        writer.OffsetBits(2);
        writer.Symbol('c');
        var expected = Expand([.. Lits("ab"u8.ToArray()), Match(2, word + 3), Lit((byte)'c')]);
        AssertDecompresses(writer.Finish(), expected.Length, expected);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(1000)]
    [InlineData(65535)]
    [InlineData(65536)]
    public void AcceptsAThirtyTwoBitLength(uint value)
    {
        // A zero 16-bit word means the length minus 3 follows as a 32-bit value; small values are again redundant but valid.
        var writer = new BlockWriter(FlatLengths());
        writer.Tokens(Lits("ab"u8.ToArray()));
        writer.Symbol(256 + 15 + 16 * 1);
        writer.Byte(255);
        writer.UInt16(0);
        writer.UInt32(value);
        writer.OffsetBits(2);
        Token[] tokens = [.. Lits("ab"u8.ToArray()), Match(2, (int)value + 3)];
        if (2 + value + 3 < BlockSize)
        {
            // A literal after the match shows that the raw bytes were consumed exactly (past 65536 bytes a new block would start).
            writer.Symbol('c');
            tokens = [.. tokens, Lit((byte)'c')];
        }

        var expected = Expand(tokens);
        AssertDecompresses(writer.Finish(), expected.Length, expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(14)]
    public void RejectsASixteenBitLengthBelowFifteen(int word)
    {
        var writer = new BlockWriter(FlatLengths());
        writer.Tokens(Lits("ab"u8.ToArray()));
        writer.Symbol(256 + 15 + 16 * 1);
        writer.Byte(255);
        writer.UInt16((ushort)word);
        writer.OffsetBits(2);
        AssertRejected(writer.Finish(), 1000);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(14u)]
    public void RejectsAThirtyTwoBitLengthBelowFifteen(uint value)
    {
        var writer = new BlockWriter(FlatLengths());
        writer.Tokens(Lits("ab"u8.ToArray()));
        writer.Symbol(256 + 15 + 16 * 1);
        writer.Byte(255);
        writer.UInt16(0);
        writer.UInt32(value);
        writer.OffsetBits(2);
        AssertRejected(writer.Finish(), 1000);
    }

    [Theory]
    [InlineData(0x7FFF_FFFCu)]
    [InlineData(0x7FFF_FFFDu)]
    [InlineData(0x7FFF_FFFEu)]
    [InlineData(0x7FFF_FFFFu)]
    [InlineData(0x8000_0000u)]
    [InlineData(0xFFFF_FFFFu)]
    public void AHugeThirtyTwoBitLengthFillsTheOutputOnlyInStrictMode(uint value)
    {
        // A length far beyond the output, up to the largest 32-bit value plus 3 (no overflow): strict decoding cuts the match at the
        // end of the output, as it does any other match; Windows fails a match that does not fit.
        var writer = new BlockWriter(FlatLengths());
        writer.Tokens(Lits("ab"u8.ToArray()));
        writer.Symbol(256 + 15 + 16 * 1);
        writer.Byte(255);
        writer.UInt16(0);
        writer.UInt32(value);
        writer.OffsetBits(2);
        var expected = Expand([.. Lits("ab"u8.ToArray()), Match(2, 998)]);
        AssertModes(writer.Finish(), 1000, strict: expected, windows: null);
    }

    #endregion

    #region Match offsets

    public static TheoryData<int> OffsetBitCounts()
    {
        var data = new TheoryData<int>();
        for (var bits = 0; bits <= 15; bits++)
        {
            data.Add(bits);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(OffsetBitCounts))]
    public void DecodesTheSmallestAndLargestOffsetForEachBitCount(int bits)
    {
        var smallest = 1 << bits;
        var largest = (1 << (bits + 1)) - 1;
        var prefix = Distinct(largest);
        Token[] tokens = [.. Lits(prefix), Match(largest, 5), Match(smallest, 4), Lit((byte)'.')];
        var expected = Expand(tokens);
        AssertDecompresses(Encode(tokens), expected.Length, expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(255)]
    [InlineData(4096)]
    public void AcceptsAnOffsetEqualToTheOutputPosition(int position)
    {
        Token[] tokens = [.. Lits(Distinct(position)), Match(position, 3)];
        AssertDecompresses(Encode(tokens), position + 3, Expand(tokens));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(255)]
    [InlineData(4096)]
    public void RejectsAnOffsetBeforeTheStartOfTheData(int position)
    {
        AssertRejected(Encode([.. Lits(Distinct(position)), Match(position + 1, 3)]), position + 3);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(1, 1000)]
    [InlineData(2, 17)]
    [InlineData(3, 18)]
    [InlineData(5, 70_000)]
    public void DecodesOverlappingCopies(int offset, int length)
    {
        Token[] tokens = [.. Lits(Distinct(offset)), Match(offset, length)];
        var expected = Expand(tokens);
        AssertDecompresses(Encode(tokens), expected.Length, expected);
        for (var i = offset; i < expected.Length; i++)
        {
            Assert.Equal(expected[i - offset], expected[i]);
        }
    }

    #endregion

    #region Bit phase of raw bytes

    public static TheoryData<int, int> PhaseCases()
    {
        var data = new TheoryData<int, int>();
        foreach (var length in new[] { 18, 272, 273, 65539 })
        {
            for (var literals = 1; literals <= 16; literals++)
            {
                data.Add(literals, length);
            }
        }

        return data;
    }

    /// <summary>
    /// Each literal costs 9 bits, so 1 to 16 literals put an extended-length match at every bit position within a 16-bit word,
    /// including the exact multiple of 16 where the decoder has not yet read the next word. The extra length bytes must be read
    /// at the decoder's read-ahead position each time, and the offset bits that follow may need a word after them.
    /// </summary>
    [Theory]
    [MemberData(nameof(PhaseCases))]
    public void ReadsExtendedLengthBytesAtEveryBitPhase(int literals, int length)
    {
        Token[] tokens = [.. Lits(Distinct(literals)), Match(literals, length), Match(1, 30), Lit((byte)'#')];
        var expected = Expand(tokens);
        AssertDecompresses(Encode(tokens), expected.Length, expected);
    }

    [Fact]
    public void ReadsBackToBackExtendedLengths()
    {
        var tokens = new List<Token> { Lit((byte)'w') };
        foreach (var length in new[] { 18, 300, 70_000, 19, 65538, 272, 273 })
        {
            tokens.Add(Match(1, length));
        }

        tokens.Add(Lit((byte)'e'));
        var expected = Expand(tokens);
        AssertDecompresses(Encode(tokens), expected.Length, expected);
    }

    #endregion

    #region Blocks

    [Theory]
    [InlineData(BlockSize - 1)]
    [InlineData(BlockSize)]
    public void DecodesOneBlockUpToItsFullSize(int length)
    {
        var data = Distinct(length);
        AssertDecompresses(Encode(Lits(data)), length, data);
    }

    [Fact]
    public void EachBlockHasItsOwnTable()
    {
        // The second block uses a table in which 'z' is the only literal: decoding it with the first table would give other bytes.
        var first = Distinct(BlockSize);
        var second = Block(LengthsOf(('z', 1), (256, 1)), Lits("zzzz"u8.ToArray()), endSymbol: true);
        AssertDecompresses(Concat(Encode(Lits(first), endSymbol: false), second), BlockSize + 4, [.. first, .. "zzzz"u8.ToArray()]);
    }

    [Fact]
    public void AMatchMayReachIntoThePreviousBlock()
    {
        var first = Distinct(BlockSize);
        var second = Encode([Match(65535, 10), Match(BlockSize - 100, 300)]);
        var expected = Expand([.. Lits(first), Match(65535, 10), Match(BlockSize - 100, 300)]);
        AssertDecompresses(Concat(Encode(Lits(first), endSymbol: false), second), expected.Length, expected);
    }

    [Fact]
    public void AMatchMayRunPastTheEndOfABlock()
    {
        // The first block ends with a match that runs 1000 bytes past 65536; the second block then covers 65536 bytes from there,
        // so a 1-literal third block is needed only after 2 * 65536 + 1000 bytes.
        Token[] firstTokens = [.. Lits(Distinct(BlockSize - 10)), Match(5, 1010)];
        var secondData = Enumerable.Range(0, BlockSize).Select(i => (byte)(i * 13 % 256)).ToArray();
        var compressed = Concat(Encode(firstTokens, endSymbol: false), Encode(Lits(secondData), endSymbol: false), Encode([Lit((byte)'!')]));
        var expected = Concat(Expand(firstTokens), secondData, "!"u8.ToArray());
        Assert.Equal(2 * BlockSize + 1001, expected.Length);
        AssertDecompresses(compressed, expected.Length, expected);
    }

    [Fact]
    public void RejectsAMissingSecondTable()
    {
        AssertRejected(Encode(Lits(Distinct(BlockSize)), endSymbol: false), BlockSize + 1);
    }

    [Fact]
    public void RejectsAShortSecondTable()
    {
        var compressed = Concat(Encode(Lits(Distinct(BlockSize)), endSymbol: false), new byte[255]);
        AssertRejected(compressed, BlockSize + 1);
    }

    [Fact]
    public void NoSecondTableIsReadWhenTheOutputEndsAtTheBlock()
    {
        // Trailing bytes too short to be a table are not looked at.
        var data = Distinct(BlockSize);
        AssertDecompresses(Concat(Encode(Lits(data), endSymbol: false), new byte[100]), BlockSize, data);
    }

    #endregion

    #region Truncated input

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    public void RejectsInputShorterThanATable(int length)
    {
        AssertRejected(new byte[length], 1);
    }

    [Fact]
    public void EmptyInputWithNoOutputDecodesToNothing()
    {
        AssertDecompresses([], 0, []);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void RejectsAnExtendedLengthCutByTheEndOfTheInput(int rawBytes)
    {
        // The match's raw length bytes run past the end of the input: the byte itself, the 16-bit word after 255, or the 32-bit
        // value after a zero word.
        var writer = new BlockWriter(FlatLengths());
        writer.Tokens(Lits("ab"u8.ToArray()));
        writer.Symbol(256 + 15 + 16 * 1);
        var header = writer.Finish(endSymbol: false);
        byte[] raw = [255, 0, 0, 0x10, 0x00, 0x00, 0x00];
        var compressed = Concat(header, raw.AsSpan(0, rawBytes).ToArray());
        AssertRejected(compressed, 1000);
    }

    public static TheoryData<int> CutLengths => new() { 1, 2, 3, 4, 8, 100, 224 };

    /// <summary>
    /// Bytes cut from the end of a 200-literal stream (which ends with its last code word and the encoder's zero word). The
    /// decoder reads that zero word ahead as it consumes the last literal, so even a 1-byte cut fails in both modes, as in Windows;
    /// before, the missing words were read as zero bits and decoded to made-up data.
    /// </summary>
    [Theory]
    [MemberData(nameof(CutLengths))]
    public void RejectsATruncatedBitStream(int cut)
    {
        var compressed = Encode(Lits(Distinct(200)), endSymbol: false);
        AssertRejected(compressed.AsSpan(0, compressed.Length - cut).ToArray(), 200);
    }

    [Fact]
    public void AShortOutputDoesNotReadTheMissingTail()
    {
        // 199 literals need no word past the last code word, so the stream without its zero word is enough.
        var compressed = Encode(Lits(Distinct(200)), endSymbol: false);
        AssertDecompresses(compressed.AsSpan(0, compressed.Length - 2).ToArray(), 199, Distinct(199));
    }

    [Theory]
    [InlineData(256)]
    [InlineData(258)]
    [InlineData(259)]
    public void RejectsATableWithoutItsFirstTwoWords(int length)
    {
        var compressed = Block(LengthsOf(('a', 1), ('b', 1)), Lits("abba"u8.ToArray()), endSymbol: false);
        AssertRejected(compressed.AsSpan(0, length).ToArray(), 1);
    }

    #endregion

    #region Output buffers

    [Fact]
    public void ZeroLengthOutputReadsNothing()
    {
        var (ok, output, consumed) = Decompress(Encode(Lits("abc"u8.ToArray())), 0);
        Assert.True(ok);
        Assert.Empty(output);
        Assert.Equal(0, consumed);
    }

    public static TheoryData<int> ShortOutputSizes => new() { 1, 5, 6, 7, 100, 1005, 1006, BlockSize, BlockSize + 1, BlockSize + 2000 };

    /// <summary>
    /// Literals, a match, a literal, a match to the end of the block, then a second block. Strict decoding fills a short output and
    /// stops, so that a caller can read the start of the data. Windows mode fails when the output ends inside a match (sizes 6 to
    /// 1004 and 1007 to 65535 here); elsewhere it reads the rest of the input as tables, which here fails too.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShortOutputSizes))]
    public void StopsAtTheEndOfAShortOutputBuffer(int outputSize)
    {
        Token[] firstTokens = [.. Lits("abcde"u8.ToArray()), Match(5, 1000), Lit((byte)'.'), Match(1006, BlockSize - 1006)];
        Token[] secondTokens = [.. Lits(Distinct(3000))];
        var compressed = Concat(Encode(firstTokens, endSymbol: false), Encode(secondTokens));
        var full = Concat(Expand(firstTokens), Expand(secondTokens));

        AssertMode(compressed, outputSize, XpressHuffmanMode.Strict, full.AsSpan(0, outputSize).ToArray());
        if (outputSize is (> 5 and < 1005) or (> 1006 and < BlockSize))
        {
            AssertMode(compressed, outputSize, XpressHuffmanMode.Windows, null);
        }

        AssertMatchesWindows(compressed, outputSize);
    }

    public static TheoryData<int> TailLengths => new() { 0, 1, 100, 259, 260, 261, 600 };

    /// <summary>
    /// A complete stream followed by junk bytes (0xFF: a table of 15-bit codes, which is oversubscribed). Strict decoding stops when
    /// the output is full. Windows mode goes on reading tables while 260 or more bytes are left, and so fails on 260 or more bytes
    /// of junk; a valid table before the junk is read, checked and skipped.
    /// </summary>
    [Theory]
    [MemberData(nameof(TailLengths))]
    public void AfterAFullOutputWindowsModeChecksTheRemainingTables(int junk)
    {
        Token[] tokens = [.. Lits("hello"u8.ToArray()), Match(5, 10)];
        var stream = Encode(tokens, endSymbol: false);
        var tail = Enumerable.Repeat((byte)0xFF, junk).ToArray();
        var expected = Expand(tokens);

        AssertModes(Concat(stream, tail), 15, strict: expected, windows: junk < 260 ? expected : null);

        var validTable = Block(LengthsOf(('a', 1), ('b', 1)), Lits("abba"u8.ToArray()), endSymbol: false).AsSpan(0, 260).ToArray();
        AssertModes(Concat(stream, validTable, tail), 15, strict: expected, windows: junk < 260 ? expected : null);
    }

    [Theory]
    [InlineData(150)]
    [InlineData(299)]
    public void AShortOutputLeavingMuchInputMatchesWindows(int outputSize)
    {
        // 300 literals cut at 150 leave about 170 bytes of code words, which Windows mode reads as a table when 260 or more are left.
        var compressed = Encode(Lits(Distinct(300)), endSymbol: false);
        AssertMode(compressed, outputSize, XpressHuffmanMode.Strict, Distinct(outputSize));
        AssertMatchesWindows(compressed, outputSize);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    public void AMatchAfterAFullOutputIsNotDecoded(int outputSize)
    {
        // Two matches of 10 after 5 literals: an output of 15 ends between them, one of 16 inside the second.
        Token[] tokens = [.. Lits("abcde"u8.ToArray()), Match(5, 10), Match(5, 10)];
        var start = Expand(tokens).AsSpan(0, outputSize).ToArray();
        AssertModes(Encode(tokens), outputSize, strict: start, windows: outputSize == 15 ? start : null);
    }

    [Fact]
    public void IgnoresTrailingBytes()
    {
        Token[] tokens = [.. Lits("trail"u8.ToArray()), Match(5, 20)];
        AssertDecompresses(Concat(Encode(tokens), [0xDE, 0xAD, 0xBE, 0xEF, 0x01]), 25, Expand(tokens));
        AssertDecompresses(Concat(Encode(tokens, endSymbol: false), [0xDE, 0xAD, 0xBE, 0xEF, 0x01]), 25, Expand(tokens));
    }

    [Fact]
    public void ReportsTheBytesConsumed()
    {
        // The vector ends with its last code word and one zero word (the encoder's flush). The decoder has read both by then.
        Token[] tokens = [.. Lits(Distinct(100)), Match(50, 300)];
        var compressed = Encode(tokens, endSymbol: false);
        var (ok, _, consumed) = Decompress(Concat(compressed, new byte[64]), 400);
        Assert.True(ok);
        Assert.Equal(compressed.Length, consumed);
    }

    [WindowsOnlyTheory]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(BlockSize)]
    [InlineData(BlockSize + 1)]
    [InlineData(150_000)]
    public void WindowsOutputDecodedIntoAShortBufferIsItsStart(int outputSize)
    {
        var data = Sample("text", 200_000);
        var compressed = NativeCompress(data, 0, data.Length, 4096, CompressionFormat.XpressHuff);
        var (ok, output, _) = Decompress(compressed, outputSize, XpressHuffmanMode.Strict);
        Assert.True(ok);
        Assert.Equal(data.AsSpan(0, outputSize).ToArray(), output);
        AssertMatchesWindows(compressed, outputSize);
    }

    [WindowsOnlyTheory]
    [MemberData(nameof(RoundTripSamples), MemberType = typeof(NativeCompression))]
    public void WindowsOutputDecodesTheSameInBothModes(string kind, int length)
    {
        var original = Sample(kind, length);
        foreach (var engine in new ushort[] { 0, EngineMaximum })
        {
            if (TryNativeCompress(original, 0, original.Length, 4096, CompressionFormat.XpressHuff, engine) is { } compressed)
            {
                AssertDecompresses(compressed, original.Length, original);
            }
        }
    }

    #endregion

    #region Instances

    [Fact]
    public void SharedInstancesHaveTheirModes()
    {
        Assert.Equal(XpressHuffmanMode.Strict, XpressHuffman.Default.Mode);
        Assert.Equal(XpressHuffmanMode.Windows, XpressHuffman.WindowsCompatible.Mode);
        Assert.Equal(XpressHuffmanMode.Strict, new XpressHuffman().Mode);
        Assert.Throws<ArgumentOutOfRangeException>(() => new XpressHuffman((XpressHuffmanMode)2));
    }

    [Fact]
    public void BlockDecompressorInterfaceUsesTheInstanceMode()
    {
        var compressed = Block(LengthsOf(('a', 1)), Lits("aaaa"u8.ToArray()), endSymbol: false);
        var output = new byte[4];
        Assert.False(((IBlockDecompressor)XpressHuffman.Default).TryDecompress(compressed, output, out _));
        Assert.True(((IBlockDecompressor)XpressHuffman.WindowsCompatible).TryDecompress(compressed, output, out var written));
        Assert.Equal(4, written);
        Assert.Equal("aaaa"u8.ToArray(), output);
    }

    #endregion

    #region XpressStream

    [Fact]
    public void StreamThrowsOnInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => new XpressStream(new MemoryStream(new byte[256 + 16]), 10));
    }

    [Fact]
    public void StreamThrowsOnTruncatedData()
    {
        var compressed = Encode(Lits(Distinct(BlockSize)), endSymbol: false);
        Assert.Throws<InvalidDataException>(() => new XpressStream(new MemoryStream(compressed), BlockSize + 10));
    }

    [Fact]
    public void StreamOfZeroLength()
    {
        var stream = new XpressStream(new MemoryStream([]), 0);
        Assert.Equal(0, stream.Length);
        Assert.Equal(0, stream.Read(new byte[10], 0, 10));
    }

    [Fact]
    public void StreamReadsHandBuiltData()
    {
        Token[] tokens = [.. Lits("stream"u8.ToArray()), Match(6, 600)];
        var expected = Expand(tokens);
        var actual = new byte[expected.Length];
        new XpressStream(new MemoryStream(Encode(tokens)), expected.Length).ReadExactly(actual);
        Assert.Equal(expected, actual);
    }

    #endregion
}
