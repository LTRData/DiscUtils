namespace DiscUtils.ReFs.Entries.Enums;

/// <summary>
///     The row type in the key of a row of a file's table: <c>u32</c> at key offset 8 of stream rows {u64 length, u32 type, marker}
///     and of the run-table rows; the hard-link record of a directory table carries its type at key offset 0 instead.
/// </summary>
internal enum RowType : uint
{
    /// <summary>Stream info row of a named stream whose data is not inline (key holds the stream id at +48); also the key of its run table.</summary>
    StreamInfo = 3,

    /// <summary>Unnamed (default) stream, reparse data and extended attribute rows (told apart by the <see cref="StreamMarker" />).</summary>
    UnnamedStream = 0x80000001,

    /// <summary>Named (alternate) data stream, and the key of the unnamed stream's run table.</summary>
    NamedStream = 0x80000002,

    /// <summary>Hard-link record {u32 type, u32 0, u64 file id, u64 holder directory} in a directory table.</summary>
    LinkRecord = 0x80000040
}