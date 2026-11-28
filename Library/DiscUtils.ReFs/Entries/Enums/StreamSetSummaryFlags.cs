namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>
///     The flags (u32 at +16) of a stream set's 40-byte control-row value: {u64 next table id, u32 tables, u32 linked tables, u32 flags, 20 bytes unused}.
///     Nothing in this reader acts on them; they are documented for the dump and for future checks. Both are 0 on every image seen.
/// </summary>
[Flags]
internal enum StreamSetSummaryFlags : uint
{
    None = 0,

    /// <summary>
    ///     A table of the set has a shadow tree: set when a shadow tree is created and cleared when it is removed. The
    ///     meaning is inferred.
    /// </summary>
    ShadowPresent = 0x1,

    /// <summary>
    ///     Presumably the set is being destroyed. The meaning is inferred.
    /// </summary>
    Destroying = 0x2
}