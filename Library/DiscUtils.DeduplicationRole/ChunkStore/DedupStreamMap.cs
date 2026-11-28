using DiscUtils.DeduplicationRole.Reparse;

namespace DiscUtils.DeduplicationRole.ChunkStore;

/// <summary>
///     The payload of a stream map record: the chunks of one file range, in file order.
///     <code>
///     +0x00 u32  signature "Smap", then 4 version bytes
///     +0x08      64-byte entries (<see cref="DedupChunk" />) up to the end of the payload or an entry with record id 0
///     </code>
/// </summary>
internal sealed class DedupStreamMap
{
    private const uint Signature = 0x70616D53; // "Smap"
    private const int HeaderLength = 8;

    /// <summary>Parses the stream map of <paramref name="range" />; its chunks must cover exactly that range.</summary>
    public DedupStreamMap(ReadOnlySpan<byte> payload, DedupStreamReference range)
    {
        if (payload.Length < HeaderLength || payload.U32(0) != Signature || (payload.Length - HeaderLength) % DedupChunk.EntryLength != 0)
            throw new DedupFormatException($"Data Deduplication stream map {range.StreamMapId} is malformed.");

        List<DedupChunk> chunks = [];
        ulong start = range.Begin;
        for (int offset = HeaderLength; offset < payload.Length; offset += DedupChunk.EntryLength)
        {
            ReadOnlySpan<byte> entry = payload.Slice(offset, DedupChunk.EntryLength);
            if (DedupChunk.IsEnd(entry))
                break;

            DedupChunk chunk = new DedupChunk(entry, start);
            chunks.Add(chunk);
            start = chunk.End;
        }

        if (start != range.End)
            throw new DedupFormatException($"Data Deduplication stream map {range.StreamMapId} covers bytes {range.Begin} to {start}; the file range ends at {range.End}.");

        Chunks = chunks;
    }

    public IReadOnlyList<DedupChunk> Chunks { get; }
}
