using DiscUtils.ReFs.Entries.Files;

namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>
///     The flags word (u16 at +2) of the attribute header a stream row's value starts with (see <see cref="FileRecord" />). Only
///     <see cref="NonResident" /> is known by meaning. The bits 0x400 and 0x800 of this word and 0x2000 are also used by Windows,
///     but their meaning is not established, so they are left unnamed here rather than guessed.
/// </summary>
[Flags]
internal enum StreamAttributeFlags : ushort
{
    None = 0,

    /// <summary>
    ///     The row is a stream snapshot, not a named stream. On the images every snapshot row has 0x1C00 and no named stream has this bit; forefst uses
    ///     the same bit ("HasSnapshot").
    /// </summary>
    Snapshot = 0x0400,

    /// <summary>The data lives in extents (the row carries a stream id); clear when it is stored inline in the row.</summary>
    NonResident = 0x1000,

    /// <summary>
    ///     The stream is EFS-encrypted: Windows decrypts the reads of a stream with this bit when a key is loaded. Not seen on the images,
    ///     where EFS files carry the Encrypted attribute and run flag 0x800 instead.
    /// </summary>
    Encrypted = 0x4000
}