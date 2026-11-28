namespace DiscUtils.ReFs.SystemTables.Enums;

/// <summary>
/// The flags (u32 at +0x14) of a container table row. Values seen on the 18 test images: 0x1, 0x2, 0x4, 0x8, 0x20, 0x40, 0x200 and 0x400, mostly as
/// the combinations 0x200 (plain data containers), 0x401, 0x420, 0x244, 0xA and 0x8. Bits not listed are not understood and are ignored (the meaning of 0x4, 0x20 and 0x200 is unknown).
/// </summary>
[Flags]
internal enum ContainerFlags : uint
{
    None = 0,

    /// <summary>
    ///     The container's data is stored as a volume-compressed chunk stream (the row also holds the format @0x30 and chunk size @0x34). The row-level meaning is inferred.
    /// </summary>
    Compressed = 0x2,

    /// <summary>
    ///     The container's runs make up a stream rather than a plain mapping (the layout the compressed form uses too); the name is this reader's. Its exact
    ///     meaning is not confirmed.
    /// </summary>
    Packed = 0x8,

    /// <summary>Inferred: a reserved container.</summary>
    Reserved = 0x40,

    /// <summary>Inferred: the container holds no allocations. Not seen on the images.</summary>
    Empty = 0x2000,

    /// <summary>Inferred: the container is fully allocated (the allocated total equals its capacity). Not seen on the images.</summary>
    Full = 0x4000
}