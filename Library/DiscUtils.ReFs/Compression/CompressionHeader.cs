using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Compression.Enums;

namespace DiscUtils.ReFs.Compression;

/// <summary>
///     One compression header row of a compressed container:
///     <c>
///         {u64 container id, u32 index, u32 7, u64 uncompressed offset, u64 uncompressed bytes,
///         u64 compressed offset, u64 compressed bytes, u32 flags, u32 chunk count, u32 64, u16 checksum type}
///     </c>
///     , followed by <c>count</c> u32 values
///     holding the absolute end offset of each compressed chunk, then (flag 2) <c>count</c> digests. The u32 7 at 0x0C is a constant (the version), the fixed length 64 at 0x38 is
///     constant,
///     the in-memory size is 64 plus 4 per chunk, and flag 2 at 0x30 is set when digests are present. The u64 at 0x00 (the container id by
///     the layout above) is not read by this reader.
/// </summary>
internal sealed class CompressionHeader
{
    private const int FixedLength = 64;
    private const uint SupportedVersion = 7;

    public CompressionHeader(ReadOnlySpan<byte> row)
    {
        if (row.Length < FixedLength)
        {
            throw new ReFsFormatException("Compression header too short.");
        }

        if (row.U32(12) != SupportedVersion || row.U32(56) != FixedLength)
        {
            throw new NotSupportedException($"Unsupported compression header version {row.U32(12)}.");
        }

        var flags = (CompressionHeaderFlags)row.U32(48);
        var count = row.U32(52);

        if (count == 0 || FixedLength + (4L * count) > row.Length)
        {
            throw new ReFsFormatException("Compression header chunk table out of range.");
        }

        Index = row.U32(8);
        UncompressedOffset = row.U64(16);
        UncompressedBytes = row.U64(24);
        CompressedOffset = row.U64(32);
        CompressedBytes = row.U64(40);
        Ends = new uint[count];
        var previous = CompressedOffset;

        for (var i = 0; i < Ends.Length; i++)
        {
            Ends[i] = row.U32(FixedLength + (4 * i));

            if (Ends[i] < previous)
            {
                throw new ReFsFormatException("Compression header chunk offsets are not increasing.");
            }

            previous = Ends[i];
        }

        if (previous != CompressedOffset + CompressedBytes)
        {
            throw new ReFsFormatException("Compression header chunk offsets do not add up to its compressed size.");
        }

        ChecksumKind = ChecksumKind.None;
        Digests = [];

        if (flags.HasFlag(CompressionHeaderFlags.HasChecksums))
        {
            ChecksumKind = (ChecksumKind)row.U16(60);
            var digestLength = PageChecksum.DigestLength(ChecksumKind);
            var digestBytes = digestLength * count;

            if (digestLength == 0 || FixedLength + (4L * count) + digestBytes > row.Length)
            {
                throw new NotSupportedException($"Unsupported compression checksum kind {(ushort)ChecksumKind}.");
            }

            Digests = row.Slice(FixedLength + (4 * (int)count), (int)digestBytes).ToArray();
        }
    }

    public uint Index { get; }

    public ulong UncompressedOffset { get; }

    public ulong UncompressedBytes { get; }

    public ulong CompressedOffset { get; }

    public ulong CompressedBytes { get; }

    /// <summary>Absolute end offset (in the container's compressed stream) of every chunk.</summary>
    public uint[] Ends { get; }

    public ChecksumKind ChecksumKind { get; }

    /// <summary>Concatenated per-chunk digests (empty when the header has none).</summary>
    public byte[] Digests { get; }

    public int ChunkCount => Ends.Length;
}