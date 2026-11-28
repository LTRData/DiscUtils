namespace DiscUtils.ReFs.BTree.Enums;

/// <summary>
/// The u16 at +8 of a node entry. A normal entry only uses <see cref="EmbeddedTable" />. A <see cref="Compact" /> entry is a 24-byte stream run row,
/// and the word is then the run's properties.
/// </summary>
[Flags]
internal enum EntryProperties : ushort
{
    None = 0,

    /// <summary>
    ///     The value is itself an embedded table (file records, stream run tables). On a run row the low two bits are key flags: Windows copies
    ///     <c>flags &amp; 3</c> into the row's key.
    /// </summary>
    EmbeddedTable = 0x01,

    /// <summary>The second key-flag bit (see <see cref="EmbeddedTable" />); meaning unknown, never seen on disk.</summary>
    Bit0x2 = 0x02,

    /// <summary>
    ///     Marker set on many <c>InsertRow</c> entries in ReFS 3.14 redo logs (e.g. 4 or 5) that is never persisted on disk
    ///     (0 or 1 there); stripped when replaying. A key flag, not a run bit: Windows' replay ORs it into the key of the row it inserts; where the
    ///     logged entries get it was not found.
    /// </summary>
    LogOnly = 0x04,

    /// <summary>A deleted range in a stream set. It masks older allocations and must never expose its stored cluster bytes.</summary>
    DeletedRange = 0x08,

    /// <summary>The run's range is valid, i.e. written.</summary>
    Valid = 0x10,

    /// <summary>
    ///     The run has no clusters behind it and reads as zeros: delayed allocation (replay maps the redo allocate flag "delayed" onto it).
    ///     Windows reports no cluster for it, and clears it together with <see cref="RecallMetadata" /> when it writes the run. forefst calls
    ///     it a sparse hole (its LCN word mirrors the virtual cluster) and has it on disk as 0x60 and 0x64.
    /// </summary>
    Delayed = 0x20,

    /// <summary>
    /// The entry is compact: the value is the 24-byte run header itself and the key is its last 12 bytes. Also marks a run inserted through the fast
    /// path.
    /// </summary>
    Compact = 0x40,

    /// <summary>The run carries one digest per checksum block after its header (integrity streams).</summary>
    Checksums = 0x80,

    /// <summary>
    ///     The alternative "run carries checksums" bit: inferred to be used instead of <see cref="Checksums" /> in some configurations (for example depending on the stream's checksum kind or a volume flag). Never seen on disk.
    /// </summary>
    OtherChecksums = 0x100,

    /// <summary>
    ///     The run carries a recall metadata header and buffer after its header, which add to the row's length. The data is not on the
    ///     volume: Windows reports no cluster for it, as for <see cref="Delayed" />, so this reader reads it as zeros. Windows clears it when it
    ///     writes the run. Never seen on disk.
    /// </summary>
    RecallMetadata = 0x200,

    /// <summary>
    ///     The run's clusters are reference counted (block cloning): Windows counts references only for runs with this bit. Never seen on disk.
    /// </summary>
    BlockRefcounted = 0x400,

    /// <summary>The run's data is encrypted. Bits from 0x1000 up are never set or tested on a run.</summary>
    Encrypted = 0x800
}