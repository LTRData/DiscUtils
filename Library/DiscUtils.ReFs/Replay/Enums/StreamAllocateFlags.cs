using DiscUtils.ReFs.BTree.Enums;

namespace DiscUtils.ReFs.Replay.Enums;

/// <summary>
///     The flags value (<c>u32</c>) of a <see cref="RedoOperation.StreamAllocate" /> record. This is not the record-level flags word (<see cref="RedoFlags" />);
///     the names follow what each bit does to the run it creates. Bits not listed are not
///     understood and are ignored, as before. The run properties the bits become are <see cref="EntryProperties" />.
/// </summary>
[Flags]
internal enum StreamAllocateFlags : uint
{
    None = 0,

    /// <summary>
    ///     The allocated range is valid (written): the run gets <see cref="EntryProperties.Valid" />. Also bit 2 of the flags of
    ///     <c>RedoStreamPersistFastRunInsertion</c>.
    /// </summary>
    Valid = 0x02,

    /// <summary>
    ///     The run was created with match-all placeholder checksums. The placeholder bytes are known (see `RunFileStream`), but no log witnessed so far
    ///     holds this flag, so replay still refuses it.
    /// </summary>
    PlaceholderChecksums = 0x10,

    /// <summary>Delayed allocation: the run gets <see cref="EntryProperties.Delayed" />.</summary>
    Delayed = 0x20,

    /// <summary>The run replaces whatever already occupies its range, instead of failing on an overlap.</summary>
    ReplaceExisting = 0x40,

    /// <summary>The run is encrypted: it gets <see cref="EntryProperties.Encrypted" />.</summary>
    Encrypted = 0x400
}