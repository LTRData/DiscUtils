using System;
using System.Collections.Generic;
using System.Text;

namespace DiscUtils.Archives;

/// <summary>
/// Tar file types.
/// </summary>
public enum TarFileType : byte
{
    /// <summary>
    /// TAR unknown entry.
    /// </summary>
    TarEntryUnknown = 0,

    /// <summary>
    /// TAR regular file entry.
    /// </summary>
    TarEntryRegularFile = (byte)'0',

    /// <summary>
    /// TAR link entry.
    /// </summary>
    TarEntryLink = (byte)'1',

    /// <summary>
    /// TAR symbolic link entry.
    /// </summary>
    TarEntrySymbolicLink = (byte)'2',

    /// <summary>
    /// TAR character special entry.
    /// </summary>
    TarEntryCharacter = (byte)'3',

    /// <summary>
    /// TAR block device entry.
    /// </summary>
    TarEntryBlock = (byte)'4',

    /// <summary>
    /// TAR directory entry.
    /// </summary>
    TarEntryDirectory = (byte)'5',

    /// <summary>
    /// TAR FIFO special entry.
    /// </summary>
    TarEntryFifo = (byte)'6',

    /// <summary>
    /// TAR contiguous file entry.
    /// </summary>
    TarEntryContiguous = (byte)'7',

    /// <summary>
    /// TAR long symbolic link entry.
    /// </summary>
    TarEntryLongLinkTarget = (byte)'K',

    /// <summary>
    /// TAR long file name entry.
    /// </summary>
    TarEntryLongLink = (byte)'L',
}
