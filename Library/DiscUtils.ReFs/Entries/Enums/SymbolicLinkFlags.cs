namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>
///     Flags word of a <c>REPARSE_DATA_BUFFER</c> symbolic link buffer (winnt.h: <c>SYMLINK_FLAG_RELATIVE</c>). On disk only <see cref="Relative" /> occurs:
///     Windows stores the word as the caller gives it and never tests it. The other bits exist only in flight: on open, Windows copies the buffer
///     for the I/O manager and ORs <see cref="Directory" /> or <see cref="File" /> into the copy, plus the file's redirection trust level shifted
///     into bits 28 to 31.
/// </summary>
[Flags]
internal enum SymbolicLinkFlags : uint
{
    None = 0,

    /// <summary>The substitute name is relative to the directory containing the symbolic link (<c>SYMLINK_FLAG_RELATIVE</c>).</summary>
    Relative = 0x1,

    /// <summary>In flight only: the link is a file link (ORed into the copy handed to the I/O manager when the target is not a directory). Never on disk.</summary>
    File = 0x40000000,

    /// <summary>In flight only: the link is a directory link (ORed into the copy handed to the I/O manager). Never on disk.</summary>
    Directory = 0x80000000
}