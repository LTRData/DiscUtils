using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DiscUtils.Compression;
using static LibraryTests.Compression.NativeCompression;

namespace LibraryTests.Compression;

/// <summary>
/// Edge cases of the LZNT1 format ([MS-XCA] 2.5) built by hand: chunk headers, the position-dependent split of a back reference
/// into displacement and length bits, overlapping copies, padding of short chunks, malformed input, terminators and short output
/// buffers. Vectors are checked against hard-coded or independently computed results on all platforms and, on Windows, against
/// <c>RtlDecompressBuffer</c> as well, except where this library deliberately differs from Windows (each such test says how).
/// </summary>
public class LZNT1EdgeCaseTest
{
#if NET471_OR_GREATER || NETCOREAPP
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
    private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
#endif

    #region Stream builder

    /// <summary>A literal byte or a back reference within an LZNT1 chunk.</summary>
    private readonly record struct Token(byte Literal, int Displacement, int Length)
    {
        public bool IsMatch => Length > 0;
    }

    private static Token Lit(byte b) => new(b, 0, 0);

    private static Token Match(int displacement, int length) => new(0, displacement, length);

    private static IEnumerable<Token> Lits(IEnumerable<byte> bytes) => bytes.Select(Lit);

    /// <summary>
    /// The number of displacement bits of a back reference at <paramref name="position"/> bytes into the chunk: the fewest bits, at
    /// least 4 and at most 12, that can hold the largest displacement possible there (the position itself), stored minus one.
    /// </summary>
    private static int DisplacementBits(int position)
    {
        var bits = 4;
        while (bits < 12 && (1 << bits) < position)
        {
            bits++;
        }

        return bits;
    }

    private static int MaxLength(int position) => (1 << (16 - DisplacementBits(position))) - 1 + 3;

    /// <summary>Encodes tokens as the body of a compressed chunk: groups of a flag byte and up to eight tokens.</summary>
    private static byte[] CompressedBody(IEnumerable<Token> tokens)
    {
        var body = new List<byte>();
        var position = 0;
        var flagIndex = -1;
        var count = 0;
        foreach (var token in tokens)
        {
            if (count % 8 == 0)
            {
                flagIndex = body.Count;
                body.Add(0);
            }

            if (token.IsMatch)
            {
                body[flagIndex] |= (byte)(1 << (count % 8));
                // Guard the vectors themselves: a token that does not fit its fields would silently encode something else.
                Assert.InRange(token.Length, 3, MaxLength(position));
                Assert.InRange(token.Displacement, 1, 1 << DisplacementBits(position));
                var lengthBits = 16 - DisplacementBits(position);
                var value = ((token.Displacement - 1) << lengthBits) | (token.Length - 3);
                body.Add((byte)value);
                body.Add((byte)(value >> 8));
                position += token.Length;
            }
            else
            {
                body.Add(token.Literal);
                position++;
            }

            count++;
        }

        return body.ToArray();
    }

    private static byte[] CompressedChunk(IEnumerable<Token> tokens) => Chunk(true, CompressedBody(tokens));

    private static byte[] UncompressedChunk(byte[] data) => Chunk(false, data);

    private static byte[] Chunk(bool compressed, byte[] body)
    {
        var header = (ushort)((compressed ? 0xB000 : 0x3000) | (body.Length - 1));
        return [(byte)header, (byte)(header >> 8), .. body];
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

            var from = output.Count - token.Displacement;
            for (var i = 0; i < token.Length; i++)
            {
                output.Add(output[from + i]);
            }
        }

        return output.ToArray();
    }

    private static byte[] Distinct(int count) => Enumerable.Range(0, count).Select(i => (byte)(i * 7 % 251 + 1)).ToArray();

    /// <summary>
    /// Tokens for <paramref name="length"/> bytes of output. A compressed chunk body holds at most 4096 bytes, so past 1000 bytes the
    /// prefix continues with matches rather than literals (each literal also costs part of a flag byte).
    /// </summary>
    private static List<Token> Prefix(int length)
    {
        var literals = Math.Min(length, 1000);
        var tokens = Lits(Distinct(literals)).ToList();
        var position = literals;
        while (position < length)
        {
            var count = Math.Min(MaxLength(position), length - position);
            if (count < 3)
            {
                tokens.Add(Lit((byte)position));
                position++;
                continue;
            }

            tokens.Add(Match(1000, count));
            position += count;
        }

        return tokens;
    }

    #endregion

    #region Helpers

    private static (bool Ok, byte[] Output) Decompress(byte[] compressed, int outputSize, LZNT1? codec = null)
    {
        var output = new byte[outputSize];
        var ok = (codec ?? LZNT1.Default).TryDecompress(compressed, output, out var written);
        return (ok, output.AsSpan(0, written).ToArray());
    }

    /// <summary>Decodes in one mode and expects <paramref name="expected"/>, or a failure when it is null.</summary>
    private static void AssertResult(LZNT1 codec, byte[] compressed, int outputSize, byte[]? expected)
    {
        var (ok, output) = Decompress(compressed, outputSize, codec);
        if (expected is null)
        {
            Assert.False(ok, $"{codec.Mode}: malformed data was accepted");
            return;
        }

        Assert.True(ok, $"{codec.Mode}: decoding failed");
        Assert.Equal(expected, output);
    }

    /// <summary>
    /// Expects <paramref name="strict"/> in <see cref="LZNT1Mode.Strict"/> mode and <paramref name="windows"/> in
    /// <see cref="LZNT1Mode.Windows"/> mode (null: the data is rejected). On Windows, Windows mode must also match <c>RtlDecompressBuffer</c>.
    /// </summary>
    private static void AssertModes(byte[] compressed, int outputSize, byte[]? strict, byte[]? windows)
    {
        AssertResult(LZNT1.Default, compressed, outputSize, strict);
        AssertResult(LZNT1.WindowsCompatible, compressed, outputSize, windows);
        AssertMatchesWindows(compressed, outputSize);
    }

    /// <summary>Both modes decode to <paramref name="expected"/>, as Windows does.</summary>
    private static void AssertDecompresses(byte[] compressed, int outputSize, byte[] expected) => AssertModes(compressed, outputSize, expected, expected);

    /// <summary>Both modes reject the data, as Windows does.</summary>
    private static void AssertRejected(byte[] compressed, int outputSize) => AssertModes(compressed, outputSize, null, null);

    /// <summary>On Windows, expects Windows mode to succeed or fail exactly when <c>RtlDecompressBuffer</c> does, with the same output.</summary>
    private static void AssertMatchesWindows(byte[] compressed, int outputSize, string context = "")
    {
        if (!IsWindows)
        {
            return;
        }

        var status = NativeDecompressStatus(compressed, outputSize, CompressionFormat.Lznt1, out var native);
        var (ok, output) = Decompress(compressed, outputSize, LZNT1.WindowsCompatible);
        Assert.True(status == 0 == ok, $"{context}Windows returned 0x{status:X8}, Windows mode returned {ok}");
        if (ok)
        {
            Assert.Equal(native, output);
        }
    }

    /// <summary>The output ranges of the back references among <paramref name="tokens"/>, which start at output position <paramref name="offset"/>.</summary>
    private static IEnumerable<(int Start, int End)> MatchSpans(IEnumerable<Token> tokens, int offset)
    {
        var position = offset;
        foreach (var token in tokens)
        {
            if (token.IsMatch)
            {
                yield return (position, position + token.Length);
                position += token.Length;
            }
            else
            {
                position++;
            }
        }
    }

    #endregion

    #region Specification example

    /// <summary>The worked example of [MS-XCA] 2.5.3: one 59-byte compressed chunk (header 0xB038).</summary>
    private static readonly byte[] SpecExampleCompressed =
    [
        0x38, 0xb0, 0x88, 0x46, 0x23, 0x20, 0x00, 0x20, 0x47, 0x20, 0x41, 0x00, 0x10, 0xa2, 0x47, 0x01,
        0xa0, 0x45, 0x20, 0x44, 0x00, 0x08, 0x45, 0x01, 0x50, 0x79, 0x00, 0xc0, 0x45, 0x20, 0x05, 0x24,
        0x13, 0x88, 0x05, 0xb4, 0x02, 0x4a, 0x44, 0xef, 0x03, 0x58, 0x02, 0x8c, 0x09, 0x16, 0x01, 0x48,
        0x45, 0x00, 0xbe, 0x00, 0x9e, 0x00, 0x04, 0x01, 0x18, 0x90, 0x00
    ];

    private const string SpecExampleText =
        "F# F# G A A G F# E D D E F# F# E E F# F# G A A G F# E D D E F# E D D E E F# D E F# G F# D E F# G F# E D E A F# F# G A A G F# E D D E F# E D D\0";

    [Fact]
    public void DecompressesTheSpecificationExample()
    {
        Assert.Equal(142, SpecExampleText.Length);
        AssertDecompresses(SpecExampleCompressed, 4096, Encoding.ASCII.GetBytes(SpecExampleText));
    }

    [Fact]
    public void SpecificationExampleRoundTripsThroughTheCompressor()
    {
        var text = Encoding.ASCII.GetBytes(SpecExampleText);
        var compressed = new byte[1024];
        Assert.Equal(CompressionResult.Compressed, LZNT1.Default.TryCompress(text, compressed, out var length));
        Assert.Equal(text, Decompress(compressed.AsSpan(0, length).ToArray(), text.Length).Output);
    }

    #endregion

    #region Displacement / length split

    /// <summary>Positions on both sides of each change in the displacement/length split (4 bits up to 16, 5 up to 32, …, 12 above 2048).</summary>
    public static TheoryData<int> SplitBoundaries => new() { 1, 2, 15, 16, 17, 31, 32, 33, 63, 64, 65, 128, 129, 256, 257, 512, 513, 1024, 1025, 2047, 2048, 2049, 4000, 4093 };

    [Theory]
    [MemberData(nameof(SplitBoundaries))]
    public void DecodesTheLargestDisplacementAtEachSplitBoundary(int position)
    {
        var length = Math.Min(MaxLength(position), 4096 - position);
        Token[] tokens = [.. Prefix(position), Match(position, length)];
        AssertDecompresses(CompressedChunk(tokens), 4096, Expand(tokens));
    }

    [Theory]
    [MemberData(nameof(SplitBoundaries))]
    public void DecodesTheSmallestDisplacementAtEachSplitBoundary(int position)
    {
        var length = Math.Min(3, 4096 - position);
        if (length < 3)
        {
            return;
        }

        // A literal after the match, where the chunk has room for one, shows that the match consumed exactly two bytes.
        Token[] tokens = position + 3 < 4096 ? [.. Prefix(position), Match(1, length), Lit(0xEE)] : [.. Prefix(position), Match(1, length)];
        AssertDecompresses(CompressedChunk(tokens), 4096, Expand(tokens));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(2048)]
    [InlineData(2049)]
    public void TheSplitIsTakenAtTheStartOfTheTokenNotAfterIt(int position)
    {
        // Two matches in a row: the second one's split depends on the output length after the first one.
        Token[] tokens = [.. Prefix(position), Match(position, 3), Match(position + 3, 3)];
        AssertDecompresses(CompressedChunk(tokens), 4096, Expand(tokens));
    }

    [Fact]
    public void TheSplitRestartsInEachChunk()
    {
        // The second chunk starts at output position 4096 but its first match is coded with chunk position 1 (4 displacement bits).
        Token[] second = [Lit((byte)'q'), Match(1, 100), .. Lits(Distinct(20)), Match(21, 20)];
        var compressed = Concat(UncompressedChunk(Distinct(4096)), CompressedChunk(second));
        AssertDecompresses(compressed, 8192, [.. Distinct(4096), .. Expand(second)]);
    }

    #endregion

    #region Overlapping copies

    [Theory]
    [InlineData(1, 3)]
    [InlineData(1, 18)]
    [InlineData(1, 4095)]
    [InlineData(2, 100)]
    [InlineData(3, 4093)]
    public void DecodesOverlappingCopies(int displacement, int length)
    {
        Token[] tokens = [.. Lits(Distinct(displacement)), Match(displacement, length)];
        var expected = Expand(tokens);
        AssertDecompresses(CompressedChunk(tokens), 4096, expected);

        for (var i = displacement; i < expected.Length; i++)
        {
            Assert.Equal(expected[i - displacement], expected[i]);
        }
    }

    [Fact]
    public void DecodesAChunkOfOnlyMatchesAfterOneLiteral()
    {
        var tokens = new List<Token> { Lit((byte)'z') };
        var position = 1;
        while (position < 4096)
        {
            var length = Math.Min(MaxLength(position), 4096 - position);
            if (length < 3)
            {
                tokens.Add(Lit((byte)'z'));
                position++;
                continue;
            }

            tokens.Add(Match(1, length));
            position += length;
        }

        var expected = Enumerable.Repeat((byte)'z', 4096).ToArray();
        Assert.Equal(expected, Expand(tokens));
        AssertDecompresses(CompressedChunk(tokens), 4096, expected);
    }

    #endregion

    #region Chunk boundaries and padding

    [Fact]
    public void DecodesFullSizeUncompressedChunks()
    {
        var first = Distinct(4096);
        var second = Enumerable.Reverse(first).ToArray();
        var compressed = Concat(UncompressedChunk(first), UncompressedChunk(second));
        Assert.Equal(0x3FFF, compressed[0] | (compressed[1] << 8));
        AssertDecompresses(compressed, 8192, [.. first, .. second]);
    }

    [Fact]
    public void DecodesAMixOfCompressedAndUncompressedChunks()
    {
        Token[] tokens = [.. Lits(Distinct(10)), Match(10, 4086)];
        var compressed = Concat(CompressedChunk(tokens), UncompressedChunk(Distinct(4096)), CompressedChunk(tokens));
        AssertDecompresses(compressed, 3 * 4096, [.. Expand(tokens), .. Distinct(4096), .. Expand(tokens)]);
    }

    /// <summary>A chunk followed by another must hold 4096 bytes ([MS-XCA] 2.5.3) in strict mode; Windows pads a shorter one with zeros.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(3600)]
    public void ShortCompressedChunkFollowedByAnother(int literals)
    {
        var compressed = Concat(CompressedChunk(Lits(Distinct(literals))), CompressedChunk(Lits("xyz"u8.ToArray())));
        AssertModes(compressed, 8192, null, [.. Distinct(literals), .. new byte[4096 - literals], .. "xyz"u8.ToArray()]);
    }

    [Fact]
    public void ShortCompressedChunkAtTheEndIsNotPadded()
    {
        AssertDecompresses(CompressedChunk(Lits(Distinct(5))), 8192, Distinct(5));
    }

    [Fact]
    public void ShortUncompressedChunkFollowedByAnother()
    {
        var compressed = Concat(UncompressedChunk("abc"u8.ToArray()), UncompressedChunk("def"u8.ToArray()));
        AssertModes(compressed, 8192, null, [.. "abc"u8.ToArray(), .. new byte[4093], .. "def"u8.ToArray()]);
    }

    [Fact]
    public void ChunkWithOnlyAFlagByteFollowedByAnother()
    {
        // A chunk body that ends right after its flag byte: no tokens, then the next chunk.
        var compressed = Concat(Chunk(true, [0x00]), CompressedChunk(Lits("abc"u8.ToArray())));
        AssertModes(compressed, 8192, null, [.. new byte[4096], .. "abc"u8.ToArray()]);
    }

    [Fact]
    public void ChunkWithOnlyAFlagByteAtTheEndDecodesToNothing()
    {
        AssertDecompresses(Chunk(true, [0x00]), 4096, []);
    }

    /// <summary>
    /// A chunk is compressed until "at least 4096 bytes" are in it ([MS-XCA] 2.5.3), so it may decode to more. Both modes decode
    /// it in full, as Windows does, and the next chunk follows directly with no padding.
    /// </summary>
    [Fact]
    public void MatchRunningPastTheEndOfTheChunk()
    {
        // One literal and a match of the largest length at position 1 (4098 bytes): the chunk decodes to 4099 bytes.
        var single = CompressedChunk([Lit((byte)'a'), Match(1, 4098)]);
        var expanded = Enumerable.Repeat((byte)'a', 4099).ToArray();
        AssertDecompresses(single, 8192, expanded);
        AssertDecompresses(Concat(single, CompressedChunk(Lits("xyz"u8.ToArray()))), 8192, [.. expanded, .. "xyz"u8.ToArray()]);
    }

    [Fact]
    public void TokensAfterTheChunkIsFullAreDecoded()
    {
        // Past 4096 bytes, back references keep 12 displacement bits, so they reach up to 4096 bytes back.
        Token[] tokens = [Lit((byte)'a'), Match(1, 4095), Lit((byte)'b'), Lit((byte)'c'), Match(2, 3), Match(4096, 18)];
        var expected = Expand(tokens);
        Assert.Equal(4096 + 2 + 3 + 18, expected.Length);
        AssertDecompresses(CompressedChunk(tokens), 8192, expected);
        AssertDecompresses(Concat(CompressedChunk(tokens), CompressedChunk(Lits("xyz"u8.ToArray()))), 8192, [.. expected, .. "xyz"u8.ToArray()]);
    }

    #endregion

    #region Malformed input

    [Fact]
    public void RejectsAMatchAtTheStartOfAChunk()
    {
        AssertRejected(CompressedChunk([Match(1, 3)]), 4096);
    }

    [Theory]
    [InlineData(5, 6)]
    [InlineData(17, 18)]
    [InlineData(100, 101)]
    [InlineData(2049, 2050)]
    public void RejectsADisplacementBeforeTheStartOfTheChunk(int position, int displacement)
    {
        AssertRejected(CompressedChunk([.. Prefix(position), Match(displacement, 3)]), 4096);
    }

    [Fact]
    public void RejectsAMatchReachingIntoThePreviousChunk()
    {
        var compressed = Concat(UncompressedChunk(Distinct(4096)), CompressedChunk([Lit(1), Match(2, 3)]));
        AssertRejected(compressed, 8192);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(1000)]
    public void RejectsAChunkLongerThanTheInput(int missing)
    {
        var chunk = CompressedChunk(Lits(Distinct(3000)));
        AssertRejected(chunk.AsSpan(0, chunk.Length - missing).ToArray(), 8192);

        var stored = UncompressedChunk(Distinct(4096));
        AssertRejected(stored.AsSpan(0, stored.Length - missing).ToArray(), 8192);
    }

    [Fact]
    public void RejectsAMatchTokenCutByTheEndOfTheChunk()
    {
        // Flag byte 0x02: a literal, then a match with only one of its two bytes inside the chunk.
        AssertRejected(Chunk(true, [0x02, (byte)'a', 0x00]), 4096);
    }

    /// <summary>The signature "MUST always be 3" ([MS-XCA] 2.5.1.2): strict mode rejects other values; Windows does not check it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void ChunkSignatureOtherThanThree(int signature)
    {
        foreach (var compressed in new[] { false, true })
        {
            var body = compressed ? CompressedBody(Lits("abcd"u8.ToArray())) : "abcd"u8.ToArray();
            var header = (ushort)((compressed ? 0x8000 : 0) | (signature << 12) | (body.Length - 1));
            AssertModes([(byte)header, (byte)(header >> 8), .. body], 4096, null, "abcd"u8.ToArray());

            // In a later chunk too.
            AssertModes([.. UncompressedChunk(Distinct(4096)), (byte)header, (byte)(header >> 8), .. body], 8192, null, [.. Distinct(4096), .. "abcd"u8.ToArray()]);
        }
    }

    [Fact]
    public void IgnoresAnOddTrailingByte()
    {
        var compressed = Concat(CompressedChunk(Lits("abc"u8.ToArray())), [0x07]);
        AssertDecompresses(compressed, 4096, "abc"u8.ToArray());
    }

    #endregion

    #region Terminators and empty buffers

    /// <summary>Input too short for a chunk header: nothing in strict mode; Windows reads a first header unconditionally and fails.</summary>
    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x07 })]
    public void InputShorterThanAHeader(byte[] compressed)
    {
        AssertModes(compressed, 4096, [], null);
    }

    [Fact]
    public void StopsAtAZeroHeader()
    {
        // Data after the terminator, even malformed, is not read.
        var compressed = Concat(CompressedChunk(Lits("abc"u8.ToArray())), [0x00, 0x00], CompressedChunk([Match(1, 3)]));
        AssertDecompresses(compressed, 8192, "abc"u8.ToArray());
    }

    /// <summary>
    /// A leading zero header is the End_of_buffer terminal in strict mode ([MS-XCA] 2.5.1.2). Windows reads the first header as a chunk
    /// even when it is zero: an uncompressed chunk of one byte.
    /// </summary>
    [Fact]
    public void ALeadingZeroHeader()
    {
        AssertModes([0x00, 0x00], 4096, [], null);
        AssertModes([0x00, 0x00, 0x05, 0xB0], 4096, [], [0x05]);
        // 05 is a short chunk followed by another (header 0706, longer than the input). Windows reads that header only when the
        // chunk after the padding would start inside the output.
        AssertModes([0x00, 0x00, 0x05, 0x06, 0x07], 4096, [], [0x05]);
        AssertModes([0x00, 0x00, 0x05, 0x06, 0x07], 8192, [], null);
        AssertModes([0x00, 0x00, 0x41, .. CompressedChunk(Lits("abc"u8.ToArray()))], 8192, [], [0x41, .. new byte[4095], .. "abc"u8.ToArray()]);
    }

    [Fact]
    public void ZeroLengthOutputWritesNothing()
    {
        AssertDecompresses(CompressedChunk(Lits("abc"u8.ToArray())), 0, []);
    }

    #endregion

    #region Short output buffers

    public static TheoryData<int> ShortOutputSizes => new() { 1, 2, 3, 4, 10, 11, 17, 100, 4095, 4096, 4097, 4099, 5000, 8191, 8192, 8200, 8242, 8243, 9000, 9241, 9242, 9259, 9260, 12288 };

    /// <summary>
    /// Strict mode fills a short output buffer and stops, so that a caller can read the start of the data. Windows also does this
    /// for literals and uncompressed data, but fails when a back reference does not fit the output.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShortOutputSizes))]
    public void ShortOutputBuffer(int outputSize)
    {
        // Literals, then a long overlapping match, then a stored chunk, then a compressed chunk with matches.
        Token[] first = [.. Lits("abcdefghij"u8.ToArray()), Match(10, 4086)];
        Token[] third = [.. Lits(Distinct(50)), Match(50, 1000), Match(1, 18)];
        var compressed = Concat(CompressedChunk(first), UncompressedChunk(Distinct(4096)), CompressedChunk(third));
        var full = Concat(Expand(first), Distinct(4096), Expand(third));

        var strict = full.AsSpan(0, Math.Min(outputSize, full.Length)).ToArray();
        var cutInsideAMatch = MatchSpans(first, 0).Concat(MatchSpans(third, 8192)).Any(s => s.Start < outputSize && outputSize < s.End);
        AssertModes(compressed, outputSize, strict, cutInsideAMatch ? null : strict);
    }

    public static TheoryData<bool, bool, int> PaddingOutputSizes()
    {
        var data = new TheoryData<bool, bool, int>();
        foreach (var (first, second) in new[] { (true, true), (false, false), (false, true), (true, false) })
        {
            foreach (var size in new[] { 2, 3, 4, 4095, 4096, 4097, 4098, 4099, 8192 })
            {
                data.Add(first, second, size);
            }
        }

        return data;
    }

    /// <summary>
    /// Two short chunks ("abc" and "def"), each compressed or stored, into outputs that end inside the first chunk, inside the
    /// padding, at the next chunk or past the end. Windows writes the padding only when the next chunk starts inside the output.
    /// Strict mode rejects the short first chunk, unless the output fills up inside it.
    /// </summary>
    [Theory]
    [MemberData(nameof(PaddingOutputSizes))]
    public void ShortOutputBufferAroundThePadding(bool firstCompressed, bool secondCompressed, int outputSize)
    {
        var first = firstCompressed ? CompressedChunk(Lits("abc"u8.ToArray())) : UncompressedChunk("abc"u8.ToArray());
        var second = secondCompressed ? CompressedChunk(Lits("def"u8.ToArray())) : UncompressedChunk("def"u8.ToArray());
        byte[] full = [.. "abc"u8.ToArray(), .. new byte[4093], .. "def"u8.ToArray()];
        var windowsLength = outputSize <= 4096 ? Math.Min(outputSize, 3) : Math.Min(outputSize, full.Length);
        var strict = outputSize <= 3 ? full.AsSpan(0, outputSize).ToArray() : null;
        AssertModes(Concat(first, second), outputSize, strict, full.AsSpan(0, windowsLength).ToArray());
    }

    [WindowsOnlyTheory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(20000)]
    public void WindowsOutputDecodedIntoAShortBuffer(int outputSize)
    {
        var data = Sample("text", 30000);
        var compressed = NativeCompress(data, 0, data.Length, 4096, CompressionFormat.Lznt1);
        AssertResult(LZNT1.Default, compressed, outputSize, data.AsSpan(0, outputSize).ToArray());
        AssertMatchesWindows(compressed, outputSize);
    }

    #endregion

    #region Windows mode differential

    /// <summary>
    /// Seeded random damage to valid streams (truncation, flipped and replaced bytes) decoded into random output sizes: Windows mode
    /// must succeed or fail exactly when <c>RtlDecompressBuffer</c> does, with the same output.
    /// </summary>
    [WindowsOnlyFact]
    public void WindowsModeMatchesWindowsOnDamagedData()
    {
        var text = Sample("text", 9000);
        var mixed = Sample("mixed", 9000);
        byte[][] seeds =
        [
            NativeCompress(text, 0, text.Length, 4096, CompressionFormat.Lznt1),
            NativeCompress(mixed, 0, mixed.Length, 4096, CompressionFormat.Lznt1),
            Concat(CompressedChunk(Lits("abc"u8.ToArray())), UncompressedChunk("def"u8.ToArray()), CompressedChunk([Lit((byte)'g'), Match(1, 40)])),
            SpecExampleCompressed,
        ];

        var random = new Random(20261128);
        for (var i = 0; i < 5000; i++)
        {
            var mutated = seeds[random.Next(seeds.Length)].ToArray();
            var damage = random.Next(4);
            if (damage == 0)
            {
                Array.Resize(ref mutated, random.Next(mutated.Length + 1));
            }
            else
            {
                for (var j = 0; j < damage && mutated.Length > 0; j++)
                {
                    var at = random.Next(Math.Min(mutated.Length, random.Next(2) == 0 ? 16 : mutated.Length));
                    mutated[at] = damage == 3 ? (byte)random.Next(256) : (byte)(mutated[at] ^ (1 << random.Next(8)));
                }
            }

            var outputSize = random.Next(3) == 0 ? random.Next(0, 20) : random.Next(0, 10000);
            AssertMatchesWindows(mutated, outputSize, $"iteration {i}, output {outputSize}, input {BitConverter.ToString(mutated, 0, Math.Min(mutated.Length, 24))}: ");
        }
    }

    #endregion

    #region Compressor

    public static TheoryData<string, int> CompressorSamples()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in new[] { "run", "period16", "period17", "period32", "period33", "period2048", "period2049", "text", "random" })
        {
            foreach (var length in new[] { 1, 2, 3, 4, 17, 4095, 4096, 4097, 8191, 8192, 8193, 12289 })
            {
                data.Add(kind, length);
            }
        }

        return data;
    }

    private static byte[] CompressorSample(string kind, int length)
    {
        if (kind == "run")
        {
            return Enumerable.Repeat((byte)'r', length).ToArray();
        }

        if (kind.StartsWith("period", StringComparison.Ordinal))
        {
            var period = int.Parse(kind.Substring("period".Length));
            var random = new Random(period);
            var unit = new byte[period];
            random.NextBytes(unit);
            return Enumerable.Range(0, length).Select(i => unit[i % period]).ToArray();
        }

        return Sample(kind, length);
    }

    [Theory]
    [MemberData(nameof(CompressorSamples))]
    public void CompressorOutputRoundTrips(string kind, int length)
    {
        var data = CompressorSample(kind, length);
        var compressed = new byte[length * 2 + 16];
        var result = LZNT1.Default.TryCompress(data, compressed, out var compressedLength);

        if (kind != "random" && length >= 64)
        {
            Assert.Equal(CompressionResult.Compressed, result);
        }

        if (result != CompressionResult.Compressed)
        {
            Assert.Equal(CompressionResult.Incompressible, result);
            return;
        }

        Assert.InRange(compressedLength, 1, length - 1);
        var output = compressed.AsSpan(0, compressedLength).ToArray();
        AssertValidChunks(output, length);
        AssertDecompresses(output, length, data);
    }

    /// <summary>Every chunk has signature 3 and fits the output, and there is one chunk per 4096 bytes of input.</summary>
    private static void AssertValidChunks(byte[] compressed, int uncompressedLength)
    {
        var chunks = 0;
        var index = 0;
        while (index + 2 <= compressed.Length)
        {
            var header = compressed[index] | (compressed[index + 1] << 8);
            if (header == 0)
            {
                break;
            }

            Assert.Equal(0x3000, header & 0x7000);
            var bodyLength = (header & 0xFFF) + 1;
            if ((header & 0x8000) == 0)
            {
                var expected = Math.Min(4096, uncompressedLength - chunks * 4096);
                Assert.Equal(expected, bodyLength);
            }

            index += 2 + bodyLength;
            Assert.True(index <= compressed.Length, "chunk runs past the output");
            chunks++;
        }

        Assert.Equal((uncompressedLength + 4095) / 4096, chunks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    public void CompressorReportsATooSmallOutputBuffer(int outputSize)
    {
        var data = CompressorSample("text", 8192);
        Assert.Equal(CompressionResult.Incompressible, LZNT1.Default.TryCompress(data, new byte[outputSize], out _));
    }

    [Fact]
    public void CompressorReportsAnOutputBufferOneByteShort()
    {
        var data = CompressorSample("text", 8192);
        var compressed = new byte[data.Length];
        Assert.Equal(CompressionResult.Compressed, LZNT1.Default.TryCompress(data, compressed, out var length));

        var result = LZNT1.Default.TryCompress(data, new byte[length - 1], out _);
        Assert.Equal(CompressionResult.Incompressible, result);
    }

    [Fact]
    public void CompressorHandlesAnEmptyInput()
    {
        Assert.NotEqual(CompressionResult.Compressed, LZNT1.Default.TryCompress([], new byte[16], out _));
    }

    [Fact]
    public void CompressorReportsATinyAllZeroInputAsIncompressible()
    {
        // The size check comes first: one zero byte cannot shrink, so the result is Incompressible. Windows reports
        // STATUS_BUFFER_ALL_ZEROS for any all-zero input.
        Assert.Equal(CompressionResult.Incompressible, LZNT1.Default.TryCompress(new byte[1], new byte[16], out _));
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(65536)]
    public void CompressorReportsAllZeros(int length)
    {
        Assert.Equal(CompressionResult.AllZeros, LZNT1.Default.TryCompress(new byte[length], new byte[length + 16], out _));
    }

    #endregion
}
