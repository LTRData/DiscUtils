using DiscUtils.ReFs.BTree.Enums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries.Streams;

namespace DiscUtils.ReFs.Entries.Files;

/// <summary>
///     A contiguous extent of a stream: <c>Count</c> clusters starting at virtual cluster <c>VirtualCluster</c>,
///     stored at container-space cluster <c>Lcn</c>. Gaps between runs are holes (read as zeros).
///     Runs of integrity streams (either bit in <see cref="ChecksumFlags" />) also carry one digest per complete fixed-size block of the run's data
///     (see <see cref="StreamChecksumPolicy" /> for the digest kind and block size).
///     <para>
///         forefst calls the run row an extent descriptor (type 0x40 extent row). The on-disk run row is a 24-byte header and the
///         digests: 0x00 u64 LCN word, 0x08 u16 properties (<see cref="EntryProperties" />), 0x0A u16 size (24 plus the digest bytes; not read: the entry is
///         already cut to it), 0x0C u64 virtual cluster, 0x14 u32 cluster count (the key), 0x18 the digests. The run table itself has properties for its
///         checksum blocks (u16 at 8, u8 at 12, u16 at 20: the block size is 1 &lt;&lt; (u16 at 20 minus u8 at 12); names inferred).
///     </para>
/// </summary>
internal readonly record struct Run
{
    /// <summary>Low 58 bits of the stored value are the cluster number; the top bits are flags.</summary>
    public const ulong LcnMask = 0x03FFFFFFFFFFFFFF;

    /// <summary>Either bit identifies a run that carries digests.</summary>
    public const EntryProperties ChecksumFlags = EntryProperties.Checksums | EntryProperties.OtherChecksums;

    /// <summary>The understood data-run properties, including inferred deleted ranges and the alternative checksum bit.</summary>
    public const EntryProperties SupportedFlags = EntryProperties.Valid | EntryProperties.Compact | ChecksumFlags | EntryProperties.DeletedRange;

    private const int HeaderLength = 24;

    /// <summary>A run from values, without a row behind it (tests, and runs rebuilt from the redo log).</summary>
    public Run(ulong virtualCluster, uint count, ulong lcn, EntryProperties flags, byte[]? checksums = null)
    {
        Lcn = lcn;
        Flags = flags;
        VirtualCluster = virtualCluster;
        Count = count;
        Checksums = checksums;
    }

    /// <summary>
    ///     Unpacks a run row. On disk it is a compact entry, where the value is the whole 24-byte header plus the digests and the key is its bytes 0x0C to
    ///     0x17; a row inserted by the redo log is a normal entry with the same key {virtual cluster, count} and the header as its value.
    /// </summary>
    /// <exception cref="ReFsFormatException">The key or the value is too short.</exception>
    public Run(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (key.Length < 12 || value.Length < 10)
        {
            throw new ReFsFormatException("Malformed run row.");
        }

        Lcn = value.U64(0x00) & LcnMask;
        Flags = (EntryProperties)value.U16(0x08);
        VirtualCluster = key.U64(0x00); // 0x0C of a compact row
        Count = key.U32(0x08); // 0x14 of a compact row
        Checksums = HasChecksums && value.Length > HeaderLength ? value[HeaderLength..].ToArray() : null; // 0x18
    }

    /// <summary>u64 @0x00, low 58 bits: the first cluster, in container space.</summary>
    public ulong Lcn { get; init; }

    /// <summary>u16 @0x08.</summary>
    public EntryProperties Flags { get; init; }

    /// <summary>u64 @0x0C (the key): the first virtual cluster of the stream this run covers.</summary>
    public ulong VirtualCluster { get; init; }

    /// <summary>u32 @0x14 (the key): the number of clusters.</summary>
    public uint Count { get; init; }

    /// <summary>The digests from 0x18 on, when either checksum-present flag is set; otherwise null.</summary>
    public byte[]? Checksums { get; init; }

    /// <summary>Original allocation and offset for a snapshot slice whose digests still cover the original checksum blocks.</summary>
    public RunIntegrityRange? IntegrityRange { get; init; }

    public bool HasChecksums => Flags.HasFlag(EntryProperties.Checksums) || Flags.HasFlag(EntryProperties.OtherChecksums);

    /// <summary>A deleted-range row still covers its virtual range while older stream tables are composed.</summary>
    public bool IsTombstone => Flags.HasFlag(EntryProperties.DeletedRange);

    public ulong EndVirtualCluster => VirtualCluster + Count;

    /// <summary>
    ///     The run has no clusters on the volume (<see cref="EntryProperties.Delayed" /> or <see cref="EntryProperties.RecallMetadata" />, tested
    ///     together, as Windows does); it reads as zeros. For a recall-metadata run the real data lives elsewhere and is not readable from the volume.
    /// </summary>
    public bool IsDelayed => Flags.HasFlag(EntryProperties.Delayed) || Flags.HasFlag(EntryProperties.RecallMetadata);

    /// <summary>The range is valid and not deleted; unwritten and deleted ranges read as zeros.</summary>
    public bool IsWritten => Flags.HasFlag(EntryProperties.Valid) && !IsTombstone;

    public bool IsSupported => (Flags & ~SupportedFlags) == EntryProperties.None;
}