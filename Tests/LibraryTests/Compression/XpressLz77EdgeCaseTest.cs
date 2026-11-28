using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DiscUtils.Compression;
using static LibraryTests.Compression.NativeCompression;

namespace LibraryTests.Compression;

/// <summary>
/// Edge cases of the XPRESS Plain LZ77 format ([MS-XCA] 2.3 and 2.4) built by hand: flag words, the length ladder (3-bit field,
/// shared half-byte, extra byte, 16-bit and 32-bit lengths), offsets, the end-of-stream marker, malformed and truncated input and
/// output buffers that are shorter or longer than the data. Vectors are checked against independently computed results on all
/// platforms and, on Windows, against <c>RtlDecompressBuffer</c> as well, except where this library deliberately differs from
/// Windows (each such test says how).
/// </summary>
public class XpressLz77EdgeCaseTest
{
#if NET471_OR_GREATER || NETCOREAPP
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
    private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
#endif

    #region Stream builder

    /// <summary>How the length of a match is written after its 16-bit token.</summary>
    public enum LengthForm
    {
        /// <summary>The shortest form, as the compressor of [MS-XCA] 2.3 writes it.</summary>
        Canonical,

        /// <summary>Half-byte 15, byte 255 and a 16-bit length (<see cref="Token.Raw"/> or the length minus 3).</summary>
        Force16,

        /// <summary>Half-byte 15, byte 255, a 16-bit zero and a 32-bit length (<see cref="Token.Raw"/> or the length minus 3).</summary>
        Force32,
    }

    /// <summary>A literal byte or a match. <see cref="Raw"/> overrides the 16-bit or 32-bit length value actually written.</summary>
    private readonly record struct Token(byte Literal, int Offset, int Length, LengthForm Form = LengthForm.Canonical, uint? Raw = null)
    {
        public bool IsMatch => Offset > 0;
    }

    private static Token Lit(byte b) => new(b, 0, 0);

    private static Token Match(int offset, int length) => new(0, offset, length);

    private static Token Match16(int offset, int length) => new(0, offset, length, LengthForm.Force16);

    private static Token Match32(int offset, int length) => new(0, offset, length, LengthForm.Force32);

    private static Token Raw16(int offset, ushort raw) => new(0, offset, 0, LengthForm.Force16, raw);

    private static Token Raw32(int offset, uint raw) => new(0, offset, 0, LengthForm.Force32, raw);

    private static IEnumerable<Token> Lits(IEnumerable<byte> bytes) => bytes.Select(Lit);

    private static IEnumerable<Token> Lits(string text) => Lits(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Encodes tokens as a Plain LZ77 stream: a little-endian 32-bit flag word (most significant bit first, 1 = match) in front of
    /// each group of 32 tokens. A match is a 16-bit token ((offset - 1) &lt;&lt; 3 | min(length - 3, 7)); when its 3-bit field is 7
    /// a half-byte follows, the first of a pair in the low half of a new byte and the second in the high half of the same byte;
    /// half-byte 15 is followed by a byte, and byte 255 by a 16-bit length minus 3 or, if that is 0, a 32-bit one.
    /// With <paramref name="terminate"/>, the remaining flags are set to 1, as the compressor does to mark the end, and a whole
    /// flag word of ones is appended when the last word is full.
    /// </summary>
    private static byte[] Encode(IEnumerable<Token> tokens, bool terminate = true)
    {
        var buffer = new List<byte>();
        var flagPosition = -1;
        uint flags = 0;
        var flagCount = 32;
        var halfBytePosition = -1;

        void StartFlagWord()
        {
            flagPosition = buffer.Count;
            buffer.AddRange(new byte[4]);
            flags = 0;
            flagCount = 0;
        }

        void FlushFlags()
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, flags);
            for (var i = 0; i < 4; i++)
            {
                buffer[flagPosition + i] = bytes[i];
            }
        }

        void AddFlag(bool isMatch)
        {
            if (flagCount == 32)
            {
                StartFlagWord();
            }

            if (isMatch)
            {
                flags |= 1u << (31 - flagCount);
            }

            flagCount++;
            FlushFlags();
        }

        foreach (var token in tokens)
        {
            AddFlag(token.IsMatch);
            if (!token.IsMatch)
            {
                buffer.Add(token.Literal);
                continue;
            }

            var length = token.Length - 3;
            var lengthField = token.Form == LengthForm.Canonical ? Math.Min(length, 7) : 7;
            var matchToken = (ushort)(((token.Offset - 1) << 3) | lengthField);
            buffer.Add((byte)matchToken);
            buffer.Add((byte)(matchToken >> 8));
            if (lengthField < 7)
            {
                continue;
            }

            length -= 7;
            var halfByte = token.Form == LengthForm.Canonical ? Math.Min(length, 15) : 15;
            if (halfBytePosition < 0)
            {
                halfBytePosition = buffer.Count;
                buffer.Add((byte)halfByte);
            }
            else
            {
                buffer[halfBytePosition] |= (byte)(halfByte << 4);
                halfBytePosition = -1;
            }

            if (halfByte < 15)
            {
                continue;
            }

            length -= 15;
            if (token.Form == LengthForm.Canonical && length < 255)
            {
                buffer.Add((byte)length);
                continue;
            }

            buffer.Add(255);
            var full = token.Raw ?? (uint)(token.Length - 3);
            if (token.Form == LengthForm.Force16 || (token.Form == LengthForm.Canonical && full < 0x10000))
            {
                buffer.Add((byte)full);
                buffer.Add((byte)(full >> 8));
            }
            else
            {
                buffer.AddRange(new byte[2]);
                var bytes = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, full);
                buffer.AddRange(bytes);
            }
        }

        if (terminate)
        {
            if (flagCount == 32)
            {
                StartFlagWord();
            }

            flags |= flagCount == 0 ? uint.MaxValue : (1u << (32 - flagCount)) - 1;
            FlushFlags();
        }

        return buffer.ToArray();
    }

    /// <summary>The data the tokens stand for, copied one byte at a time so that overlapping matches repeat.</summary>
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

            for (var i = 0; i < token.Length; i++)
            {
                output.Add(output[output.Count - token.Offset]);
            }
        }

        return output.ToArray();
    }

    /// <summary><paramref name="count"/> varied bytes, written as literals.</summary>
    private static byte[] Distinct(int count) => Enumerable.Range(0, count).Select(i => (byte)(i * 7 + (i >> 8) * 13)).ToArray();

    #endregion

    #region Helpers

    private static (bool Ok, byte[] Output) Decompress(byte[] compressed, int outputSize, XpressLz77Mode mode = XpressLz77Mode.Windows)
    {
        var output = new byte[outputSize];
        var ok = XpressLz77.TryDecompress(compressed, output, out var written, mode);
        return (ok, output.AsSpan(0, written).ToArray());
    }

    /// <summary>Expects both modes to give the same result: Windows and [MS-XCA] only disagree on a stream without data.</summary>
    private static (bool Ok, byte[] Output) DecompressInBothModes(byte[] compressed, int outputSize)
    {
        var windows = Decompress(compressed, outputSize);
        var strict = Decompress(compressed, outputSize, XpressLz77Mode.Strict);
        Assert.Equal(windows.Ok, strict.Ok);
        Assert.Equal(windows.Output, strict.Output);
        return windows;
    }

    /// <summary>Decodes with this library only, in both modes, and expects <paramref name="expected"/>.</summary>
    private static void AssertDecoded(byte[] compressed, int outputSize, byte[] expected)
    {
        var (ok, output) = DecompressInBothModes(compressed, outputSize);
        Assert.True(ok, "decoding failed");
        Assert.Equal(expected, output);
    }

    /// <summary>Decodes with this library and expects <paramref name="expected"/>; on Windows, also expects the same result as Windows.</summary>
    private static void AssertDecompresses(byte[] compressed, int outputSize, byte[] expected)
    {
        AssertDecoded(compressed, outputSize, expected);
        AssertMatchesWindows(compressed, outputSize);
    }

    private static void AssertDecompresses(IEnumerable<Token> tokens)
    {
        var list = tokens.ToList();
        var expected = Expand(list);
        AssertDecompresses(Encode(list), expected.Length, expected);
    }

    private static void AssertRejected(byte[] compressed, int outputSize)
    {
        Assert.False(DecompressInBothModes(compressed, outputSize).Ok, "malformed data was accepted");
        AssertMatchesWindows(compressed, outputSize);
    }

    /// <summary>
    /// On Windows, expects this library in <see cref="XpressLz77Mode.Windows"/> mode to succeed or fail exactly when
    /// <c>RtlDecompressBuffer</c> does, with the same output.
    /// </summary>
    private static void AssertMatchesWindows(byte[] compressed, int outputSize)
    {
        if (!IsWindows)
        {
            return;
        }

        var status = NativeDecompressStatus(compressed, outputSize, CompressionFormat.Xpress, out var native);
        var (ok, output) = Decompress(compressed, outputSize);
        Assert.True(status == 0 == ok, $"Windows returned 0x{status:X8}, this library returned {ok}");
        if (ok)
        {
            Assert.Equal(native, output);
        }
    }

    #endregion

    #region Specification examples

    /// <summary>[MS-XCA] 3.1: 26 literals; the flag word 0x0000003F marks the end after them.</summary>
    [Fact]
    public void DecompressesTheAlphabetExample()
    {
        var text = Encoding.ASCII.GetBytes("abcdefghijklmnopqrstuvwxyz");
        byte[] compressed = [0x3f, 0x00, 0x00, 0x00, .. text];
        AssertDecompresses(compressed, text.Length, text);
        Assert.Equal(compressed, Encode(Lits(text)));
    }

    /// <summary>[MS-XCA] 3.2: "abc" and a match {offset 3, length 297} written as half-byte 15, byte 255 and 16-bit 294.</summary>
    [Fact]
    public void DecompressesTheRepeatedAbcExample()
    {
        byte[] compressed = [0xff, 0xff, 0xff, 0x1f, 0x61, 0x62, 0x63, 0x17, 0x00, 0x0f, 0xff, 0x26, 0x01];
        var expected = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("abc", 100)));
        AssertDecompresses(compressed, expected.Length, expected);
        Assert.Equal(compressed, Encode([.. Lits("abc"), Match(3, 297)]));
    }

    /// <summary>Flags are read as a little-endian word and used from the most significant bit: 0x7FFFFFFF is a literal, then a match.</summary>
    [Fact]
    public void FlagsAreLittleEndianAndUsedFromTheTopBit()
    {
        AssertDecompresses([0xff, 0xff, 0xff, 0x7f, (byte)'a', 0x00, 0x00], 4, "aaaa"u8.ToArray());
    }

    #endregion

    #region Flag words

    /// <summary>Literals that end inside, exactly at and just after a flag word, so the end marker shares the last word or gets its own.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void DecodesLiteralsAroundFlagWordBoundaries(int count)
    {
        AssertDecompresses(Lits(Distinct(count)));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(32)]
    public void DecodesAMatchAtTheEndOrStartOfAFlagWord(int literals)
    {
        AssertDecompresses([.. Lits(Distinct(literals)), Match(3, 20), Lit(1)]);
    }

    [Fact]
    public void DecodesFlagWordsOfOnlyMatches()
    {
        AssertDecompresses([.. Lits("abc"), .. Enumerable.Range(0, 70).Select(i => Match(1 + i % 3, 3 + i % 5))]);
    }

    #endregion

    #region Match lengths

    /// <summary>
    /// Every step of the length ladder: the 3-bit field (3-9), the half-byte (10-24), the extra byte (25-279), the 16-bit length
    /// (280-65538) and the 32-bit length (65539 and up), each at its edges.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(279)]
    [InlineData(280)]
    [InlineData(281)]
    [InlineData(65538)]
    [InlineData(65539)]
    [InlineData(70000)]
    public void DecodesEachStepOfTheLengthLadder(int length)
    {
        AssertDecompresses([.. Lits("abc"), Match(3, length)]);
    }

    /// <summary>Longer forms than needed are valid as long as the 16-bit or 32-bit value is at least 15 + 7.</summary>
    [Theory]
    [InlineData(LengthForm.Force16, 25)]
    [InlineData(LengthForm.Force16, 26)]
    [InlineData(LengthForm.Force16, 279)]
    [InlineData(LengthForm.Force32, 25)]
    [InlineData(LengthForm.Force32, 280)]
    [InlineData(LengthForm.Force32, 65538)]
    public void DecodesNonCanonicalLengthForms(LengthForm form, int length)
    {
        AssertDecompresses([.. Lits("abc"), new Token(0, 3, length, form), Lit(9)]);
    }

    /// <summary>[MS-XCA] 2.4 and Windows reject a 16-bit or 32-bit length below 15 + 7 (the 32-bit 0 included).</summary>
    [Theory]
    [InlineData(LengthForm.Force16, 1u)]
    [InlineData(LengthForm.Force16, 21u)]
    [InlineData(LengthForm.Force32, 0u)]
    [InlineData(LengthForm.Force32, 1u)]
    [InlineData(LengthForm.Force32, 21u)]
    public void RejectsAnExtendedLengthBelowTheShorterForms(LengthForm form, uint raw)
    {
        AssertRejected(Encode([.. Lits("abc"), new Token(0, 3, 0, form, raw)]), 400);
    }

    [Theory]
    [InlineData(LengthForm.Force16, 22u)]
    [InlineData(LengthForm.Force32, 22u)]
    public void AcceptsTheSmallestExtendedLength(LengthForm form, uint raw)
    {
        AssertDecompresses(Encode([.. Lits("abc"), new Token(0, 3, 0, form, raw)]), 400, Expand([.. Lits("abc"), Match(3, 25)]));
    }

    /// <summary>32-bit lengths whose sign bit is set or that overflow when 3 or the output position is added must fail, not throw.</summary>
    [Theory]
    [InlineData(0x7FFF_FFFCu)]
    [InlineData(0x7FFF_FFFFu)]
    [InlineData(0x8000_0000u)]
    [InlineData(0xFFFF_FFFCu)]
    [InlineData(0xFFFF_FFFFu)]
    public void RejectsHuge32BitLengths(uint raw)
    {
        var compressed = Encode([.. Lits("abc"), Raw32(3, raw)]);
        AssertRejected(compressed, 400);
        Assert.Throws<InvalidDataException>(() => XpressLz77.Decompress(compressed, new byte[400]));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(300)]
    [InlineData(70000)]
    public void RejectsAMatchOneByteLongerThanTheOutput(int length)
    {
        Token[] tokens = [.. Lits("abc"), Match(3, length)];
        AssertRejected(Encode(tokens), Expand(tokens).Length - 1);
    }

    #endregion

    #region Shared length half-byte

    /// <summary>
    /// Three extended matches: the first reads a new byte and uses its low half, the second uses the high half of that byte, the
    /// third reads a new byte again.
    /// </summary>
    [Fact]
    public void AThirdExtendedMatchReadsANewHalfByte()
    {
        Token[] tokens = [.. Lits("abc"), Match(3, 10), Match(3, 24), Match(3, 13)];
        var compressed = Encode(tokens);
        byte[] expected = [0xff, 0xff, 0xff, 0x1f, 0x61, 0x62, 0x63, 0x17, 0x00, 0xe0, 0x17, 0x00, 0x17, 0x00, 0x03];
        Assert.Equal(expected, compressed);
        AssertDecompresses(compressed, Expand(tokens).Length, Expand(tokens));
    }

    [Fact]
    public void TheHalfBytePairSpansLiteralsAndShortMatches()
    {
        AssertDecompresses([.. Lits("abcde"), Match(5, 12), Lit(1), Match(2, 4), Lits("xy").First(), Match(7, 22), Lit(2)]);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void TheHalfBytePairSpansAFlagWord(int literals)
    {
        AssertDecompresses([.. Lits(Distinct(literals)), Match(3, 10), Match(3, 20), Lit(1), Match(4, 15)]);
    }

    /// <summary>The high half-byte can be 15 too, followed by its own extra byte, 16-bit or 32-bit length.</summary>
    [Theory]
    [InlineData(25)]
    [InlineData(279)]
    [InlineData(280)]
    [InlineData(65539)]
    public void TheSecondHalfByteCanChainToALongerLength(int length)
    {
        AssertDecompresses([.. Lits("abc"), Match(3, 10), Match(3, length), Match(3, 10)]);
    }

    [Fact]
    public void BothHalfBytesCanChainToLongerLengths()
    {
        AssertDecompresses([.. Lits("abc"), Match(3, 300), Match(2, 70000), Match(1, 50), Match(3, 280)]);
    }

    #endregion

    #region Offsets

    [Theory]
    [InlineData(1, 3)]
    [InlineData(1, 5000)]
    [InlineData(2, 9)]
    [InlineData(3, 100)]
    public void DecodesOverlappingCopies(int offset, int length)
    {
        AssertDecompresses([.. Lits("xyz"), Match(offset, length)]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(100)]
    [InlineData(8191)]
    [InlineData(8192)]
    public void DecodesAnOffsetEqualToThePosition(int position)
    {
        AssertDecompresses([.. Lits(Distinct(position)), Match(position, 30)]);
    }

    [Theory]
    [InlineData(8192)]
    [InlineData(9000)]
    public void DecodesTheLargestOffset(int position)
    {
        AssertDecompresses([.. Lits(Distinct(position)), Match(8192, 300)]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    [InlineData(100, 101)]
    [InlineData(8191, 8192)]
    public void RejectsAnOffsetBeforeTheStartOfTheOutput(int position, int offset)
    {
        AssertRejected(Encode([.. Lits(Distinct(position)), Match(offset, 3)]), position + 3);
    }

    #endregion

    #region Truncated and malformed input

    /// <summary>
    /// A stream with every kind of field (literals, short and extended matches, the shared high half-byte, extra byte, 16-bit and
    /// 32-bit lengths), cut at every byte, never fills the output. A cut just before a match token reads as the end-of-stream
    /// marker, so that is a valid shorter stream; every other cut is rejected.
    /// </summary>
    [Fact]
    public void NoTruncationFillsTheOutput()
    {
        Token[] tokens = [.. Lits("abc"), Match(3, 5), Match(3, 30), Lit(7), Match(4, 400), Match(1, 70000), Match(3, 12), Lit(8)];
        var compressed = Encode(tokens, terminate: false);
        var full = Expand(tokens);
        AssertDecompresses(Encode(tokens), full.Length, full);
        var accepted = 0;
        for (var length = 0; length < compressed.Length; length++)
        {
            var cut = compressed.AsSpan(0, length).ToArray();
            var (ok, output) = DecompressInBothModes(cut, full.Length);
            if (ok)
            {
                accepted++;
                Assert.True(output.Length < full.Length);
                Assert.Equal(full.AsSpan(0, output.Length).ToArray(), output);
            }

            Assert.Throws<InvalidDataException>(() => XpressLz77.Decompress(cut, new byte[full.Length]));
            AssertMatchesWindows(cut, full.Length);
        }

        Assert.Equal(5, accepted);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsACutFlagWord(int length)
    {
        var compressed = Encode(Lits(Distinct(33)), terminate: false);
        AssertRejected(compressed.AsSpan(0, 36 + length).ToArray(), 33);
    }

    [Fact]
    public void RejectsEmptyInputForANonEmptyOutput()
    {
        AssertRejected([], 5);
    }

    #endregion

    #region End of stream and output size

    /// <summary>A match flag at the end of the input marks the end; as in Windows, a larger output reports the data's length.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    public void TheEndMarkerStopsALargerOutput(int count)
    {
        var data = Distinct(count);
        AssertDecompresses(Encode(Lits(data)), count + 100, data);
    }

    [Fact]
    public void DecompressThrowsWhenTheDataDoesNotFillTheOutput()
    {
        Assert.Throws<InvalidDataException>(() => XpressLz77.Decompress(Encode(Lits("abc")), new byte[4]));
    }

    [Fact]
    public void RejectsAMissingEndMarkerWithALargerOutput()
    {
        AssertRejected(Encode(Lits("abc"), terminate: false), 10);
    }

    /// <summary>
    /// Decoding runs to the end marker even when the output is already full, so a missing marker, data after the output is full
    /// and an output too short for the data are rejected, as in Windows and [MS-XCA] 2.4.
    /// </summary>
    [Fact]
    public void RejectsDataBeyondAFullOutput()
    {
        var abc = Lits("abc").ToArray();
        AssertRejected(Encode(abc, terminate: false), 3);
        AssertRejected([.. Encode(abc), 1, 2, 3, 4, 5], 3);
        AssertRejected([.. Encode(abc), 0, 0], 3);
        AssertRejected(Encode(abc), 0);
        AssertRejected(Encode(abc), 2);
        AssertRejected([], 0);
    }

    /// <summary>
    /// The one place where the modes differ: [MS-XCA] 2.4 reads a stream that holds only an end marker as empty data, while
    /// Windows rejects it with STATUS_BAD_COMPRESSION_BUFFER, whatever the output size.
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0xff, 0xff, 0xff, 0xff }, 0)]
    [InlineData(new byte[] { 0xff, 0xff, 0xff, 0xff }, 5)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x80 }, 1)]
    public void AnEndMarkerAloneIsEmptyDataOnlyInStrictMode(byte[] compressed, int outputSize)
    {
        var (ok, output) = Decompress(compressed, outputSize, XpressLz77Mode.Strict);
        Assert.True(ok);
        Assert.Empty(output);
        Assert.False(Decompress(compressed, outputSize).Ok);
        AssertMatchesWindows(compressed, outputSize);

        XpressLz77.Decompress(compressed, Span<byte>.Empty, XpressLz77Mode.Strict);
        Assert.Throws<InvalidDataException>(() => XpressLz77.Decompress(compressed, Span<byte>.Empty));
        Assert.True(((IBlockDecompressor)XpressLz77.Default).TryDecompress(compressed, new byte[outputSize], out _));
        Assert.False(((IBlockDecompressor)XpressLz77.WindowsCompatible).TryDecompress(compressed, new byte[outputSize], out _));
    }

    /// <summary>Every output shorter than the data is rejected, whether it ends between tokens or inside a match.</summary>
    [Fact]
    public void RejectsEveryShortOutput()
    {
        Token[] tokens = [.. Lits("abc"), Match(3, 10), Lits("xyz").First(), Lit(2), Match(1, 30), Match(5, 300)];
        var compressed = Encode(tokens);
        var full = Expand(tokens);
        for (var outputSize = 0; outputSize < full.Length; outputSize++)
        {
            AssertRejected(compressed, outputSize);
        }
    }

    [Fact]
    public void ModesAndInstances()
    {
        Assert.Equal(XpressLz77Mode.Windows, XpressLz77.WindowsCompatible.Mode);
        Assert.Equal(XpressLz77Mode.Strict, XpressLz77.Default.Mode);
        Assert.Equal(XpressLz77Mode.Windows, new XpressLz77().Mode);
        Assert.Equal(XpressLz77Mode.Strict, new XpressLz77(XpressLz77Mode.Strict).Mode);
    }

    [Fact]
    public void DecodesThroughTheBlockDecompressorInterface()
    {
        Token[] tokens = [.. Lits("abc"), Match(3, 300)];
        foreach (var decompressor in new IBlockDecompressor[] { XpressLz77.WindowsCompatible, XpressLz77.Default })
        {
            var output = new byte[303];
            Assert.True(decompressor.TryDecompress(Encode(tokens), output, out var written));
            Assert.Equal(303, written);
            Assert.Equal(Expand(tokens), output);
        }
    }

    #endregion

    #region Windows compressor

    /// <summary>The Windows compressor pads the last flag word with ones, adding a word of ones when the last one is full.</summary>
    [WindowsOnlyTheory]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    public void WindowsCompressorEndsWithTheSameMarker(int count)
    {
        var data = Distinct(count);
        Assert.Equal(Encode(Lits(data)), NativeCompress(data, 0, data.Length, 4096, CompressionFormat.Xpress));
    }

    /// <summary>
    /// Runs of zeros become a literal and one match, written with the same length forms as <see cref="Encode"/>. (Windows stores
    /// shorter runs, below about 64 bytes, as literals.)
    /// </summary>
    [WindowsOnlyTheory]
    [InlineData(64)]
    [InlineData(279)]
    [InlineData(280)]
    [InlineData(281)]
    [InlineData(65539)]
    [InlineData(65540)]
    [InlineData(70000)]
    public void WindowsCompressorWritesTheSameLengthForms(int count)
    {
        var data = new byte[count];
        var compressed = NativeCompress(data, 0, data.Length, 4096, CompressionFormat.Xpress);
        Assert.Equal(Encode([Lit(0), Match(1, count - 1)]), compressed);
        AssertDecoded(compressed, count, data);
    }

    #endregion
}
