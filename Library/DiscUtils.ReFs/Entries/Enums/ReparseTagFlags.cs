namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>
///     The property bits in the top four bits of a reparse tag (winnt.h: <c>IsReparseTagMicrosoft</c>, <c>IsReparseTagNameSurrogate</c>,
///     <c>IsReparseTagDirectory</c>); the low 16 bits are the tag number.
/// </summary>
[Flags]
internal enum ReparseTagFlags : uint
{
    None = 0,

    /// <summary>A directory may carry the tag while it is not empty (<c>IsReparseTagDirectory</c>).</summary>
    Directory = 0x10000000,

    /// <summary>The tag redirects to another file or directory, like a symlink or mount point (<c>IsReparseTagNameSurrogate</c>).</summary>
    NameSurrogate = 0x20000000,

    /// <summary>Reserved; formerly "high latency" (slow to retrieve, as for hierarchical storage).</summary>
    HighLatency = 0x40000000,

    /// <summary>
    ///     The tag is owned by Microsoft (<c>IsReparseTagMicrosoft</c>). A tag without it is a third-party tag, and its buffer is a
    ///     <c>REPARSE_GUID_DATA_BUFFER</c>: a 16-byte GUID follows the 8-byte header, before the data.
    /// </summary>
    Microsoft = 0x80000000
}