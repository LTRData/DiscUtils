using DiscUtils.DeduplicationRole.Compression;

namespace DiscUtils.DeduplicationRole.ChunkStore.Enums;

/// <summary>
///     How a Data Deduplication chunk's payload is compressed (u32 at +0x20 of its record header). Each value is one less than Windows'
///     <c>COMPRESSION_FORMAT_*</c> number for the format (LZNT1 2, XPRESS 3, XPRESS_HUFF 4); the numbering is the open-source readers', and the lab
///     image <c>w2025-dedup</c> confirms only <see cref="None" /> and <see cref="Xpress" />.
/// </summary>
internal enum DedupCompressionFormat : uint
{
    None = 0,

    /// <summary>LZNT1 of [MS-XCA] 2.5 (<see cref="DedupDecompressors"/>; not seen on the lab image).</summary>
    Lznt1 = 1,

    /// <summary>Plain LZ77 of [MS-XCA] 2.4 (<see cref="DedupDecompressors"/>).</summary>
    Xpress = 2,

    /// <summary>LZ77 with Huffman coding of [MS-XCA] 2.1 and 2.2 (<see cref="DedupDecompressors"/>; not seen on the lab image).</summary>
    XpressHuffman = 3
}
