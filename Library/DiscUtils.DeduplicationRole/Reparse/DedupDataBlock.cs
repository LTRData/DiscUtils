using DiscUtils.DeduplicationRole.Reparse.Enums;

namespace DiscUtils.DeduplicationRole.Reparse;

/// <summary>
///     The "DdRp" property block of Data Deduplication reparse data (<see cref="DedupPropertyBlock" />), inside "FeRp". Properties by index:
///     <code>
///     0  Version  revision (1)
///     1  IdRef    {u32 sequence number, u32 stream id}
///     2… Blob     64-byte range entries (<see cref="DedupStreamReference" />; one on every lab file)
///     </code>
/// </summary>
internal sealed record DedupDataBlock
{
    private const uint Signature = 0x70526444; // "DdRp"
    private const int IdRefIndex = 1;
    private const int FirstRangeIndex = 2;

    public DedupDataBlock(ReadOnlyMemory<byte> bytes)
    {
        DedupPropertyBlock block = new DedupPropertyBlock(bytes, Signature, "DdRp");
        ReadOnlySpan<byte> idRef = block.Get(IdRefIndex, DedupPropertyType.IdRef, 8).Span;
        Sequence = idRef.U32(0);
        StreamId = idRef.U32(4);

        List<DedupStreamReference> ranges = [];
        for (int i = FirstRangeIndex; i < block.Count; i++)
            if (block.Has(i))
                ranges.Add(new DedupStreamReference(block.Get(i, DedupPropertyType.Blob, DedupStreamReference.EntryLength).Span));

        Ranges = ranges;
    }

    public uint Sequence { get; }

    public uint StreamId { get; }

    public IReadOnlyList<DedupStreamReference> Ranges { get; }
}
