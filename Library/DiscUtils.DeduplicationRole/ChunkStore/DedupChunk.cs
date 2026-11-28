namespace DiscUtils.DeduplicationRole.ChunkStore;

/// <summary>
///     One chunk of a stream map: the data record {id, container id, offset, generation}, the file range [<see cref="Start" />, <see cref="End" />) it
///     fills and its hash. On disk a 64-byte entry of a stream map's payload:
///     <code>
///     +0x00 u32  data record id (0: no more entries)
///     +0x04 u32  data container id
///     +0x08 u32  offset of the record in the container
///     +0x0C u32  container generation
///     +0x10 u64  end of the chunk in the file (the start is the previous entry's end, or the range's start)
///     +0x18      the first 32 bytes of the SHA-512 of the chunk's data
///     +0x38 u32  stored (possibly compressed) size
///     +0x3C u32  (not known)
///     </code>
/// </summary>
internal sealed record DedupChunk
{
    public const int EntryLength = 64;

    /// <summary>Parses an entry whose chunk starts at <paramref name="start" />.</summary>
    public DedupChunk(ReadOnlySpan<byte> entry, ulong start)
    {
        Id = entry.U32(0);
        ContainerId = entry.U32(4);
        Offset = entry.U32(8);
        Generation = entry.U32(0xC);
        Start = start;
        End = entry.U64(0x10);
        Hash = entry.Slice(0x18, 32).ToArray();
        StoredSize = entry.U32(0x38);
        if (End <= Start || End - Start > int.MaxValue)
            throw new DedupFormatException($"Data Deduplication chunk {Id} ends at {End}, after its start {Start}.");
    }

    public uint Id { get; }

    public uint ContainerId { get; }

    public uint Offset { get; }

    public uint Generation { get; }

    public ulong Start { get; }

    public ulong End { get; }

    public byte[] Hash { get; }

    public uint StoredSize { get; }

    public int Length => (int)(End - Start);

    /// <summary>Whether the entry ends the map (record id 0).</summary>
    public static bool IsEnd(ReadOnlySpan<byte> entry)
    {
        return entry.U32(0) == 0;
    }
}