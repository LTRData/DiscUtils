namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>What a stream row is: the marker after the <see cref="RowType" /> in its key.</summary>
internal enum StreamMarker : ushort
{
    /// <summary>Unnamed data stream.</summary>
    UnnamedData = 0x80,

    /// <summary>Named data stream (the stream name follows the key header).</summary>
    NamedData = 0xB0,

    /// <summary>Reparse data (symbolic link, mount point, ...).</summary>
    ReparseData = 0xC0,

    /// <summary>Extended attributes.</summary>
    ExtendedAttributes = 0xE0
}