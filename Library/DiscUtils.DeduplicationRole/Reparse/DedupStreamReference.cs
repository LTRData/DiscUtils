namespace DiscUtils.DeduplicationRole.Reparse;

/// <summary>
///     Where the chunk store keeps one range of a deduplicated file: the stream map record {id, container id, offset in the container, container
///     generation} in the chunk store's stream containers, and the file range [<see cref="Begin" />, <see cref="End" />) its chunks cover.
///     On disk a 64-byte range entry of the "DdRp" block:
///     <code>
///     +0x00 u32  stream map record id
///     +0x04 u32  stream container id (from 0x10000)
///     +0x08 u32  offset of the record in the container
///     +0x0C u32  container generation
///     +0x10 u32, u32 (not known)
///     +0x18 u32  stream map payload length
///     +0x1C u32  (not known)
///     +0x20      the first 16 bytes of the stream map's digest
///     +0x30 u64  end of the range in the file
///     +0x38 u64  start of the range
///     </code>
/// </summary>
internal readonly record struct DedupStreamReference
{
    public const int EntryLength = 0x40;

    /// <summary>Parses a 64-byte range entry.</summary>
    public DedupStreamReference(ReadOnlySpan<byte> entry)
    {
        if (entry.Length != EntryLength)
            throw new DedupFormatException($"A Data Deduplication range entry is {entry.Length} bytes long; expected {EntryLength}.");

        StreamMapId = entry.U32(0x0);
        ContainerId = entry.U32(0x4);
        Offset = entry.U32(0x8);
        Generation = entry.U32(0xC);
        Begin = entry.U64(0x38);
        End = entry.U64(0x30);
    }

    /// <summary>The first 16 bytes of a range entry (all the short reparse format keeps), for the range [<paramref name="begin" />, <paramref name="end" />).</summary>
    public DedupStreamReference(ReadOnlySpan<byte> entry, ulong begin, ulong end)
    {
        StreamMapId = entry.U32(0x0);
        ContainerId = entry.U32(0x4);
        Offset = entry.U32(0x8);
        Generation = entry.U32(0xC);
        Begin = begin;
        End = end;
    }

    public uint StreamMapId { get; }

    public uint ContainerId { get; }

    public uint Offset { get; }

    public uint Generation { get; }

    public ulong Begin { get; }

    public ulong End { get; }
}