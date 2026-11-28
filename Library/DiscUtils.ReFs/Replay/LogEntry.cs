using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.Replay;

/// <summary>
///     A valid log entry: one or more contiguous 4 KiB log pages holding a payload of redo blocks. From the first page's header
///     (<see cref="RedoLog" />): +0x20 u32 open generation, +0x28 u64 entry LSN, +0x30 u64 previous entry LSN, +0x38 u32 pages allocated,
///     +0x3C u32 pages used.
/// </summary>
internal sealed record LogEntry
{
    /// <summary>Parses an entry from its pages (already checked by <see cref="RedoLog" />) and the payload's place in them.</summary>
    public LogEntry(ReadOnlySpan<byte> pages, int payloadOffset, int payloadSize)
    {
        Generation = pages.U32(0x20);
        Lsn = pages.U64(0x28);
        PreviousLsn = pages.U64(0x30);
        AllocatedPages = (int)pages.U32(0x38);
        Pages = (int)pages.U32(0x3C);
        Payload = pages.Slice(payloadOffset, payloadSize).ToArray();
    }

    /// <summary>
    ///     Pages the entry occupies in the log (+0x38, at least <see cref="Pages" />): the next entry starts after them, as Windows reads the log.
    /// </summary>
    public int AllocatedPages { get; }

    public ulong Lsn { get; }

    public ulong PreviousLsn { get; }

    public uint Generation { get; }

    /// <summary>Pages that hold the entry's data (+0x3C); the checksum covers these.</summary>
    public int Pages { get; }

    public byte[] Payload { get; }
}