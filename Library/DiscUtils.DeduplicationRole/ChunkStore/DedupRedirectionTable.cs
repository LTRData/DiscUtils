namespace DiscUtils.DeduplicationRole.ChunkStore;

/// <summary>
///     A container's redirection table "Rrtl" (at 0x3000, a backup at 0x4000): where records moved when the container was compacted.
///     <code>
///     +0x00 u32  signature "Rrtl", then 4 version bytes
///     +0x08 u32  number of items
///     +0x0C u32  item length (8)
///     +0x18 u64  CRC-64 of the bytes before it
///     +0x20      {u32 record id, u32 offset}[number]
///     </code>
/// </summary>
internal sealed class DedupRedirectionTable
{
    /// <summary>The table's header, which gives the length of its items (<see cref="TryGetItemsLength" />).</summary>
    public const int HeaderLength = 0x20;

    /// <summary>Where a container keeps the table and its backup.</summary>
    public static readonly long[] Offsets = [0x3000, 0x4000];

    private const uint Signature = 0x6C747252; // "Rrtl"
    private const int ItemLength = 8;
    private readonly Dictionary<uint, uint> _offsets = [];

    /// <summary>Parses the items that follow a valid header (<see cref="TryGetItemsLength" />).</summary>
    public DedupRedirectionTable(ReadOnlySpan<byte> items)
    {
        for (int i = 0; i + ItemLength <= items.Length; i += ItemLength)
        {
            // The first entry for an id wins.
            uint id = items.U32(i);
            if (!_offsets.ContainsKey(id))
                _offsets[id] = items.U32(i + 4);
        }
    }

    /// <summary>The length of the items after the header; false when the bytes are not a redirection table.</summary>
    public static bool TryGetItemsLength(ReadOnlySpan<byte> header, out int length)
    {
        bool table = header.Length >= HeaderLength && header.U32(0) == Signature && header.U32(0xC) == ItemLength && header.U32(8) <= int.MaxValue / ItemLength;
        length = table ? (int)header.U32(8) * ItemLength : 0;
        return table;
    }

    /// <summary>The offset the table gives the record; null when it does not list it.</summary>
    public uint? Find(uint recordId)
    {
        return _offsets.TryGetValue(recordId, out uint offset) ? offset : null;
    }
}
