using DiscUtils.ReFs.Entries.Files;

namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>
///     The flags (u32 at +16) of the 24-byte tail every row key of a stream set ends with: {u64 table id, u64 parent id, u32 flags, u32 0}. The tail is
///     the composite-key entry of type 14. Windows reads them when it opens a set. See <see cref="FileRecord.CheckStreamSets" />.
/// </summary>
[Flags]
internal enum StreamSetKeyFlags : uint
{
    /// <summary>A table as it is attached to the set.</summary>
    None = 0,

    /// <summary>
    ///     The set's control row, whose key tail is {8, 8, 1}. The name
    ///     is inferred: only the control key carries the bit.
    /// </summary>
    Control = 0x1,

    /// <summary>
    ///     A deleted stream snapshot's merge is prepared. Not seen on the images.
    /// </summary>
    MergePending = 0x2,

    /// <summary>
    ///     The merge is committed. Windows
    ///     counts these tables separately and refuses to open the set unless the count matches. Not seen on the images.
    /// </summary>
    Linked = 0x4,

    /// <summary>
    ///     The table has a shadow tree. The meaning is inferred. Not seen on the images.
    /// </summary>
    Shadow = 0x8
}