using DiscUtils.DeduplicationRole.ChunkStore.Enums;

namespace DiscUtils.DeduplicationRole.ChunkStore;

/// <summary>
///     The header of a "Ckhr" record in a chunk store container:
///     <code>
///     +0x00 u32  signature "Ckhr", then 4 version bytes
///     +0x08 u32  record id
///     +0x0C u32  payload length (the payload follows the header)
///     +0x10 u16  record type
///     +0x12 u16  header length beyond the fixed 0x30 bytes
///     +0x20      type-specific fields; a data chunk: u32 compression format at +0x20, 32-byte hash at +0x28
///     -0x10 u64  CRC-64 of the payload
///     -0x08 u64  CRC-64 of the header before this field
///     </code>
/// </summary>
internal sealed record DedupRecordHeader
{
    /// <summary>The fixed part, which gives the full header length (<see cref="TryGetLength" />).</summary>
    public const int FixedLength = 0x30;

    private const uint Signature = 0x72686B43; // "Ckhr"
    private const int CompressionOffset = 0x20;
    private const int ChunkHashOffset = 0x28;
    private const int ChunkHashLength = 32;
    private const int CrcLength = 16;

    /// <summary>Parses a complete header (of the length <see cref="TryGetLength" /> gave).</summary>
    public DedupRecordHeader(ReadOnlySpan<byte> header)
    {
        if (!TryGetLength(header, out int length) || header.Length != length || length < FixedLength + CrcLength)
            throw new DedupFormatException("Malformed Data Deduplication record header.");

        Id = header.U32(8);
        PayloadLength = header.U32(0xC);
        Type = (DedupRecordType)header.U16(0x10);
        PayloadCrc = header.U64(length - CrcLength);
        HeaderCrc = header.U64(length - 8);
        if (Type != DedupRecordType.DataChunk)
            return;

        if (length < ChunkHashOffset + ChunkHashLength + CrcLength)
            throw new DedupFormatException($"Data Deduplication chunk {Id} has a {length}-byte header, too short for its hash.");

        Compression = (DedupCompressionFormat)header.U32(CompressionOffset);
        ChunkHash = header.Slice(ChunkHashOffset, ChunkHashLength).ToArray();
    }

    public uint Id { get; }

    public uint PayloadLength { get; }

    public DedupRecordType Type { get; }

    /// <summary>How a data chunk's payload is compressed (<see cref="DedupCompressionFormat.None" /> for other records).</summary>
    public DedupCompressionFormat Compression { get; }

    /// <summary>A data chunk's hash: the first 32 bytes of the SHA-512 of its data (empty for other records).</summary>
    public byte[] ChunkHash { get; } = [];

    public ulong PayloadCrc { get; }

    /// <summary>CRC-64 of the header bytes before this field.</summary>
    public ulong HeaderCrc { get; }

    /// <summary>The full header length from the record's fixed part; false when the bytes are not a "Ckhr" record.</summary>
    public static bool TryGetLength(ReadOnlySpan<byte> fixedPart, out int length)
    {
        bool record = fixedPart.Length >= FixedLength && fixedPart.U32(0) == Signature;
        length = record ? FixedLength + fixedPart.U16(0x12) : 0;
        return record;
    }
}
