using DiscUtils.Compression;

namespace DiscUtils.DeduplicationRole.Compression;

/// <summary>
/// Decompresses Data Deduplication chunks with the DiscUtils decoders ([MS-XCA] LZNT1, plain LZ77 XPRESS and LZ77+Huffman XPRESS). Each method
/// fills exactly <c>output</c> or throws <see cref="DedupFormatException"/>.
/// </summary>
internal static class DedupDecompressors
{
    public static void Lznt1(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (!LZNT1.Default.TryDecompress(input, output, out var written) || written != output.Length)
        {
            throw new DedupFormatException("A Data Deduplication LZNT1 chunk is malformed or does not fill its length.");
        }
    }

    public static void Xpress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        bool decoded;
        int written;
        try
        {
            decoded = XpressLz77.TryDecompress(input, output, out written);
        }
        catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or ArgumentException)
        {
            throw new DedupFormatException("A Data Deduplication XPRESS chunk is malformed.", ex);
        }

        if (!decoded || written != output.Length)
        {
            throw new DedupFormatException("A Data Deduplication XPRESS chunk is malformed or does not fill its length.");
        }
    }

    public static void XpressHuffman(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (!DiscUtils.Compression.XpressHuffman.TryDecompress(input, output, out _, out var written) || written != output.Length)
        {
            throw new DedupFormatException("A Data Deduplication XPRESS Huffman chunk is malformed or does not fill its length.");
        }
    }
}
