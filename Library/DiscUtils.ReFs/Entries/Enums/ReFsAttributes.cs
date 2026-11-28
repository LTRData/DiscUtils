namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>
///     The attributes word of a file or directory (u32 in the standard information at +0x20 and in the directory record at +0x40). Windows uses the Win32 file attribute values as its
///     vocabulary and applies a mask
///     on every read, write and create. Members here are the bits kept by those masks plus the ReFS-only bits; values not listed (0x8,
///     0x10000) are not used.
/// </summary>
[Flags]
internal enum ReFsAttributes : uint
{
    None = 0,
    ReadOnly = 0x1,
    Hidden = 0x2,
    System = 0x4,

    /// <summary>Not stored: set on read when the node is a directory. See <see cref="DirectoryMarker" />.</summary>
    Directory = 0x10,
    Archive = 0x20,

    /// <summary>Allowed when a file is created, but never reported or settable; its purpose is unknown.</summary>
    Device = 0x40,

    /// <summary>Only reported when no other attribute is set.</summary>
    Normal = 0x80,

    /// <summary>Settable, but reported from a stream flag rather than from the stored bit.</summary>
    Temporary = 0x100,

    /// <summary>Reported from a stream flag (it is requested at create time), not from the stored bit.</summary>
    SparseFile = 0x200,
    ReparsePoint = 0x400,

    /// <summary>Allowed at create; never reported by the query paths.</summary>
    Compressed = 0x800,
    Offline = 0x1000,
    NotContentIndexed = 0x2000,

    /// <summary>Reported from a stream flag, not from the stored bit.</summary>
    Encrypted = 0x4000,

    /// <summary>Reported when the stream has integrity information; not settable.</summary>
    IntegrityStream = 0x8000,
    NoScrubData = 0x20000,

    /// <summary>
    ///     ReFS-only: stored on files and directories that have extended attributes (seen on the ReFS 3.10 and 3.14 lab images; the code that writes it is not known). It is outside the
    ///     read mask and Windows does not report it, so this reader strips it from reported attributes.
    /// </summary>
    ExtendedAttributesMarker = 0x40000,

    /// <summary>Win32 <c>FILE_ATTRIBUTE_PINNED</c>; kept by the attribute masks, and settable.</summary>
    Pinned = 0x80000,

    /// <summary>Win32 <c>FILE_ATTRIBUTE_UNPINNED</c>; kept by the attribute masks, and settable.</summary>
    Unpinned = 0x100000,

    /// <summary>Win32 <c>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS</c>; only kernel-mode callers can set it.</summary>
    RecallOnDataAccess = 0x400000,

    /// <summary>
    ///     ReFS-only: the marker stored in a directory's row. This reader tests it to tell a directory row from a hard-link row and then
    ///     reports <see cref="Directory" /> instead.
    /// </summary>
    DirectoryMarker = 0x10000000,

    /// <summary>ReFS-only, derived on read (not stored): set when the stream is flagged and the volume is on SMR (shingled) disks. The name is inferred.</summary>
    SmrStream = 0x20000000,

    /// <summary>ReFS-only, derived on read (not stored): set when the stream is flagged and the volume is on remote zones. The name is inferred.</summary>
    RemoteZoneStream = 0x40000000
}