using System.Text;
using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries.Enums;
using DiscUtils.ReFs.SystemTables;

namespace DiscUtils.ReFs.Entries.Files;

/// <summary>
///     A file record: the value of a file row in a directory table. It is an embedded table whose payload is the standard information
///     and whose rows describe the data streams, their runs, the reparse data and the extended attributes.
///     <para>
///         Row keys: {u64, u32 <see cref="RowType" /> @8, u16 <see cref="StreamMarker" /> @12, u16 0, UTF-16 stream name @16 for named streams}.
///         The unnamed stream's run table has a 0x28-byte key of type <see cref="RowType.NamedStream" />; a named stream's run table has
///         type <see cref="RowType.StreamInfo" /> and the stream id at key offset 48.
///     </para>
///     <para>
///         The value of a stream row is a 12-byte attribute header followed by the stream record. Header: byte 0 = 0 (Windows treats a
///         non-zero byte as an unusable attribute; none was seen, and this reader does not check it), u16 at +2 =
///         <see
///             cref="StreamAttributeFlags" />
///         , u32 at +4 = payload length, u16 at +8 = payload offset (12). Header checks: total length at least 0x30, value
///         offset at least 0xB; u32 at +4 is the payload length rounded to 8 plus 12. Record fields: u32 at 0x00 a length-like field that must keep the value
///         inside the attribute, and u32 at 0x0C read only when <see cref="StreamAttributeFlags.NonResident" /> is clear; 0x04 to 0x2B a 40-byte stream
///         summary block (three parts: 16, 16 and 8 bytes, the same layout as the summary in the run table payload) that holds the allocated size at 0x18 and
///         the size at 0x20; u32 at 0x38 = 2 on the images; at 0x3C the inline data of a resident stream, or the u64 stream id of a non-resident one.
///     </para>
/// </summary>
internal readonly ref struct FileRecord
{
    private const int KeyHeaderLength = 16;
    private const int UnnamedRunTableKeyLength = 0x28;
    private const int RunTableStreamIdOffset = 48;

    /// <summary>In the 0x28-byte key of a run table of the unnamed stream: u32 version id at 0x10, u32 id of the version it was split from at 0x18.</summary>
    private const int RunTableVersionOffset = 0x10;

    private const int RunTableParentOffset = 0x18;
    private const uint LiveVersion = 0x1000;

    /// <summary>Last 24 bytes of a stream-set key: {u64 table id, u64 parent, u64 flag}.</summary>
    private const int SetTailLength = 24;

    /// <summary>The table id and parent of a stream set's control row, and the parent of its oldest table: not an allocated id (those start at 0x1000).</summary>
    private const ulong NoTableId = 8;

    private const int ControlRowLength = 40;

    /// <summary>In a snapshot row's value: u32 version id at 0x44, FILETIME of the snapshot at 0x4C.</summary>
    private const int SnapshotVersionOffset = 0x44;

    private const int SnapshotTimeOffset = 0x4C;

    private const int FlagsOffset = 2;
    private const int SizeOffset = 0x20;
    private const int DataOffset = 0x3C;

    /// <summary>The 40-byte stream summary starts at 0x10 of a stream row's value; in it the allocation is at +8 and the size at +0x10.</summary>
    private const int SummaryOffset = 0x10;

    private const int SummaryAllocatedOffset = 8;
    private const int SummarySizeOffset = 0x10;

    /// <summary>
    ///     ReFS 3.1 to 3.5 (from Server 2016, Server 2019 and build 20236 images): a 12-byte key header, the unnamed stream's
    ///     run table directly in the row of attribute type 0x80 (same run rows as later versions; its 96-byte payload has the stream summary at
    ///     4 instead of 0: allocated at 0x0C, size at 0x14, which is where the redo log of these versions writes them), and named streams stored inline as
    ///     an attribute cut into chunk rows of at most 4,072 bytes.
    /// </summary>
    private const int LegacyKeyHeaderLength = 12;

    private const ushort LegacyDataAttribute = 0x80;
    private const ushort LegacyNamedDataAttribute = 0xB0;
    private const ushort LegacyReparseAttribute = 0xC0;
    private const int LegacyRunTableSummaryOffset = 4;

    /// <summary>Inline streams stop at 128 KiB (Windows refuses a larger resident size); the bound keeps a corrupt length from allocating much more.</summary>
    private const uint MaximumChunkedAttributeLength = 0x40000;

    /// <summary>A stream row is listed when its value reaches past the size field.</summary>
    private const int ListedStreamLength = SizeOffset + sizeof(ulong);

    private readonly TreeReader _tree;
    private readonly EmbeddedTable _table;

    public FileRecord(TreeReader tree, ReadOnlySpan<byte> value)
    {
        _tree = tree;
        _table = new EmbeddedTable(value);
        Info = StandardInformation.Parse(_table.Payload, tree.StandardInformationMinimumLength);
    }

    public StandardInfoV34 Info { get; }

    private TreeRows Rows => _tree.Rows(_table.Value, _table.NodeOffset);

    /// <summary>What a row of a file record (or of a directory's own metadata row, which is laid out the same way) is.</summary>
    public static FileRowKind Classify(EntryView row)
    {
        var key = row.Key;

        // ReFS 3.1 to 3.5 key: {u32 attribute length, u32 chunk offset, u16 attribute type, u16 0, UTF-16 name}. The word at 10 is
        // zero there and is the high half of the row type (0x8000) in the later layout, except for row type 3, which is not 0x80 or 0xB0.
        // A key that looks legacy but matches no legacy row is read as the later layout.
        if (key.Length >= LegacyKeyHeaderLength && key.U16(10) == 0 && ClassifyLegacy(key) is var legacy and not FileRowKind.Other)
        {
            return legacy;
        }

        if (key.Length < KeyHeaderLength)
        {
            return FileRowKind.Other;
        }

        var marker = (StreamMarker)key.U16(12);

        return (RowType)key.U32(8) switch
        {
            // The extended attributes marker fills the whole u32 at 12.
            RowType.UnnamedStream when key.Length == KeyHeaderLength => marker switch
            {
                StreamMarker.ReparseData => FileRowKind.ReparseData,
                StreamMarker.UnnamedData => FileRowKind.UnnamedData,
                StreamMarker.ExtendedAttributes when key.U16(14) == 0 => FileRowKind.ExtendedAttributes,
                _ => FileRowKind.Other
            },
            RowType.NamedStream when !row.IsEmbeddedTable => key.Length > KeyHeaderLength && marker == StreamMarker.NamedData ? FileRowKind.NamedData : FileRowKind.Other,
            RowType.NamedStream => key.Length == UnnamedRunTableKeyLength ? FileRowKind.UnnamedRunTable : FileRowKind.Other,
            RowType.StreamInfo when row.IsEmbeddedTable && key.Length >= RunTableStreamIdOffset + sizeof(ulong) => FileRowKind.NamedRunTable,
            _ => FileRowKind.Other
        };
    }

    /// <summary>A ReFS 3.1 to 3.5 row by its attribute type; <see cref="FileRowKind.Other" /> when it is none this reader knows.</summary>
    private static FileRowKind ClassifyLegacy(ReadOnlySpan<byte> key)
    {
        // The unnamed run table and the reparse point row have an empty name (key length 14); the reparse value is laid out as in the later versions.
        var emptyName = key.Length == LegacyKeyHeaderLength + 2 && key.U16(LegacyKeyHeaderLength) == 0;

        return key.U16(8) switch
        {
            LegacyDataAttribute when emptyName => FileRowKind.LegacyUnnamedRunTable,
            LegacyReparseAttribute when emptyName => FileRowKind.ReparseData,
            LegacyNamedDataAttribute when key.Length > LegacyKeyHeaderLength && (key.Length & 1) == 0 => FileRowKind.LegacyNamedData,
            _ => FileRowKind.Other
        };
    }

    /// <summary>
    ///     Checks each stream set's control row against the tables stored for it. A set (the unnamed stream's, or a named stream's) is a control row and
    ///     one embedded table per version, with keys that differ only in their last 24 bytes, the composite-key entry of type 14 : {u64 table id, u64
    ///     parent (8 = none), u32 <see cref="StreamSetKeyFlags" />, u32 0}. The control row ends {8, 8, 1} . Its 40-byte value is {u64 next table id, u32
    ///     tables, u32 linked tables, u32 <see cref="StreamSetSummaryFlags" />, ...}. When opening a set, Windows counts every table and, separately, the
    ///     <see cref="StreamSetKeyFlags.Linked" /> ones, and fails the open unless both match the control row. Records without control
    ///     rows (ReFS 3.1 to 3.5) have nothing to check.
    /// </summary>
    /// <returns>The number of sets that have a control row (each one checked).</returns>
    /// <exception cref="ReFsFormatException">A control row's table or linked-table count differs from the tables stored for its set.</exception>
    public int CheckStreamSets()
    {
        Dictionary<string, StreamSetCounts> sets = [];

        foreach (var row in Rows)
        {
            var key = row.Key;

            if (key.Length < KeyHeaderLength + SetTailLength)
            {
                continue;
            }

            var type = (RowType)key.U32(8);
            var shape = type == RowType.NamedStream ? key.Length == UnnamedRunTableKeyLength : type == RowType.StreamInfo && key.Length >= RunTableStreamIdOffset + sizeof(ulong) + SetTailLength;

            if (!shape)
            {
                continue;
            }

            var tail = key.Length - SetTailLength;
            var flags = (StreamSetKeyFlags)key.U32(tail + 16);
            var control = !row.IsEmbeddedTable && key.U64(tail) == NoTableId && key.U64(tail + 8) == NoTableId && flags.HasFlag(StreamSetKeyFlags.Control);
            var table = row.IsEmbeddedTable && !flags.HasFlag(StreamSetKeyFlags.Control);

            if (!control && !table)
            {
                continue;
            }

            var set = Convert.ToHexString(key[8..tail]);
            var counts = sets.GetValueOrDefault(set);

            if (control)
            {
                if (row.Value.Length < ControlRowLength)
                {
                    throw new ReFsFormatException($"Stream set control row is {row.Value.Length} bytes long; expected at least {ControlRowLength}.");
                }

                counts = counts with { HasControl = true, Tables = row.Value.U32(8), Linked = row.Value.U32(12) };
            }
            else
            {
                counts = counts with { PresentTables = counts.PresentTables + 1, PresentLinked = counts.PresentLinked + (flags.HasFlag(StreamSetKeyFlags.Linked) ? 1u : 0u) };
            }

            sets[set] = counts;
        }

        var checkedSets = 0;

        foreach (var (set, counts) in sets)
        {
            if (!counts.HasControl)
            {
                continue;
            }

            if (counts.Tables != counts.PresentTables)
            {
                throw new ReFsFormatException(
                    $"A stream set's control row counts {counts.Tables} table(s) but {counts.PresentTables} are present (key {set}); Windows refuses to open such a stream.");
            }

            if (counts.Linked != counts.PresentLinked)
            {
                throw new ReFsFormatException(
                    $"A stream set's control row counts {counts.Linked} linked table(s) but {counts.PresentLinked} are linked (key {set}); Windows refuses to open such a stream.");
            }

            checkedSets++;
        }

        return checkedSets;
    }

    /// <summary>The data streams in row order. An unnamed stream without a row of its own (not stored inline, or empty) comes first, described by the standard information.</summary>
    public List<ReFsStreamInfo> GetStreams() => ListStreams(out _, out _);

    /// <summary>The data streams (as <see cref="GetStreams" />) and the reparse point (as <see cref="GetReparsePoint" />), from one walk over the rows.</summary>
    public (List<ReFsStreamInfo> Streams, ReFsReparsePointDto? Reparse) GetStreamsAndReparsePoint()
    {
        var streams = ListStreams(out var reparse, out var hasReparse);
        return (streams, hasReparse ? ReFsReparsePointDto.From(ReparsePoint.TryParse(reparse)) : null);
    }

    /// <summary>The data streams; also hands back the value of the last reparse point row, which the same walk passes.</summary>
    private List<ReFsStreamInfo> ListStreams(out ReadOnlySpan<byte> reparse, out bool hasReparse)
    {
        List<ReFsStreamInfo> streams = [];
        var hasUnnamed = false;
        reparse = default;
        hasReparse = false;

        foreach (var row in Rows)
        {
            var kind = Classify(row);

            if (kind == FileRowKind.ReparseData)
            {
                reparse = row.Value;
                hasReparse = true;
                continue;
            }

            // An inline stream longer than one row is cut into chunk rows; it is listed once, from its first chunk (which holds the
            // attribute header and the size).
            if (kind is FileRowKind.UnnamedData or FileRowKind.NamedData or FileRowKind.LegacyNamedData && IsContinuationChunk(row.Key))
            {
                continue;
            }

            if (kind == FileRowKind.LegacyNamedData)
            {
                if (row.Value.Length >= ListedStreamLength)
                {
                    streams.Add(new ReFsStreamInfo(LegacyStreamName(row.Key), row.Value.U64(SizeOffset), true));
                }

                continue;
            }

            if (kind is not (FileRowKind.UnnamedData or FileRowKind.NamedData) || row.Value.Length < ListedStreamLength)
            {
                continue;
            }

            if (IsSnapshot(row.Value))
            {
                streams.Add(new ReFsStreamInfo(StreamName(row.Key), row.Value.U64(SizeOffset), false, true, new DateTimeOffset(DateTime.FromFileTimeUtc((long)row.Value.U64(SnapshotTimeOffset)))));
            }
            else
            {
                streams.Add(new ReFsStreamInfo(StreamName(row.Key), row.Value.U64(SizeOffset), IsResident(row.Value)));
            }

            hasUnnamed |= kind == FileRowKind.UnnamedData;
        }

        if (!hasUnnamed)
        {
            // The size the stream reads with (the run table's summary); a record that cannot be read lists the standard-information size.
            ulong size;

            try
            {
                size = UnnamedStream().Size;
            }
            catch (Exception ex) when (ex is ReFsFormatException or NotSupportedException or ArgumentOutOfRangeException)
            {
                size = Info.Length;
            }

            streams.Insert(0, new ReFsStreamInfo(string.Empty, size, false));
        }

        return streams;
    }

    /// <summary>The reparse point of the file; null when it has none (or its data is malformed).</summary>
    public ReFsReparsePointDto? GetReparsePoint()
    {
        ReFsReparsePointDto? reparse = null;

        foreach (var row in Rows)
        {
            if (Classify(row) == FileRowKind.ReparseData)
            {
                reparse = ReFsReparsePointDto.From(ReparsePoint.TryParse(row.Value));
            }
        }

        return reparse;
    }

    /// <summary>The reparse point row of the file with its raw data (for tags whose data this library decodes elsewhere); null when it has none.</summary>
    internal ReparsePoint? GetReparsePointRow()
    {
        ReparsePoint? point = null;

        foreach (var row in Rows)
        {
            if (Classify(row) == FileRowKind.ReparseData)
            {
                point = ReparsePoint.TryParse(row.Value);
            }
        }

        return point;
    }

    /// <summary>Finds a data stream by name (case-insensitive; null or empty for the unnamed stream, which every file has).</summary>
    public bool TryFindStream(string? name, out FileRecordStream stream, UpcaseTable? upcase = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            stream = UnnamedStream();
            return true;
        }

        var fold = upcase ?? UpcaseTable.Invariant;

        foreach (var row in Rows)
        {
            var kind = Classify(row);

            if (kind is not (FileRowKind.NamedData or FileRowKind.LegacyNamedData) || IsContinuationChunk(row.Key) || row.Value.Length < DataOffset)
            {
                continue;
            }

            var stored = kind == FileRowKind.LegacyNamedData ? LegacyStreamName(row.Key) : StreamName(row.Key);

            if (!fold.Equal(stored, name!))
            {
                continue;
            }

            var value = AttributeValue(row, kind);
            CheckSummary(value[SummaryOffset..]);

            if (kind == FileRowKind.LegacyNamedData)
            {
                stream = new FileRecordStream { Name = stored, Size = value.U64(SizeOffset), IsResident = true, ResidentData = ResidentData(value), IsEncrypted = IsEncrypted(value) };
                return true;
            }

            if (IsSnapshot(value))
            {
                // A snapshot has no table of its own kind: it is a version of the unnamed stream.
                ReadOnlySpan<byte> table = default;
                uint parent = 0;
                var found = TryFindVersion(value.U32(SnapshotVersionOffset), out table, out parent);
                stream = new FileRecordStream
                    { Name = stored, Size = value.U64(SizeOffset), RunTable = CheckedRunTable(table), ParentVersion = found ? parent : null, IsEncrypted = IsEncrypted(value) };

                return true;
            }

            stream = IsResident(value)
                ? new FileRecordStream { Name = stored, Size = value.U64(SizeOffset), IsResident = true, ResidentData = ResidentData(value), IsEncrypted = IsEncrypted(value) }
                : new FileRecordStream
                    { Name = stored, Size = value.U64(SizeOffset), RunTable = CheckedRunTable(FindRunTable(FileRowKind.NamedRunTable, value.U64(DataOffset))), IsEncrypted = IsEncrypted(value) };

            return true;
        }

        stream = default;
        return false;
    }

    /// <summary>
    ///     The unnamed stream: its inline data when it has a row of its own, otherwise its run table and the size from the run table's stream summary
    ///     (what <c>RefsUpdateFcbInfoFromDisk</c> loads on open). Standard information can lag behind it: a write-through or lazy-written extension updates
    ///     only the summary until the next flush or cleanup, and Windows then lists the old size but reads the new one (lab image w2025-dirtyopen).
    /// </summary>
    private FileRecordStream UnnamedStream()
    {
        // One walk collects all three layouts; they are then taken in order: an inline row, the ReFS 3.1 to 3.5 run table, the versioned run tables.
        // Errors in the later layouts are kept until the earlier ones are ruled out, so the result and the exceptions match reading them one by one.
        ReadOnlySpan<byte> inline = default;
        var hasRow = false;
        var inlineEncrypted = false;
        ReadOnlySpan<byte> legacy = default;
        var hasLegacy = false;

        // The live version is 0x1000. A record whose only table has another id (not seen) is read as before: the last table.
        ReadOnlySpan<byte> table = default;
        uint parent = 0;
        var live = false;
        var emptyTable = false;

        foreach (var row in Rows)
        {
            switch (Classify(row))
            {
                case FileRowKind.UnnamedData when !IsContinuationChunk(row.Key) && row.Value.Length >= DataOffset:
                    var value = AttributeValue(row, FileRowKind.UnnamedData);
                    CheckSummary(value[SummaryOffset..]);
                    inline = ResidentData(value);
                    inlineEncrypted = IsEncrypted(value);
                    hasRow = true;
                    break;

                // ReFS 3.1 to 3.5: one run table in the row of attribute type 0x80, and no versions. The first one counts.
                case FileRowKind.LegacyUnnamedRunTable when !hasLegacy:
                    legacy = row.Value;
                    hasLegacy = true;
                    break;

                case FileRowKind.UnnamedRunTable when !live && !emptyTable:
                    emptyTable = row.Value.IsEmpty;
                    table = row.Value;
                    parent = row.Key.U32(RunTableParentOffset);
                    live = row.Key.U32(RunTableVersionOffset) == LiveVersion;
                    break;
            }
        }

        if (hasRow)
        {
            return new FileRecordStream { Name = string.Empty, Size = (ulong)inline.Length, IsResident = true, ResidentData = inline, IsEncrypted = inlineEncrypted };
        }

        if (hasLegacy)
        {
            if (legacy.IsEmpty)
            {
                throw new ReFsFormatException("Run table row without an embedded table.");
            }

            var summary = new EmbeddedTable(legacy).Payload[LegacyRunTableSummaryOffset..];
            CheckSummary(summary);
            return new FileRecordStream { Name = string.Empty, Size = SummarySize(summary), RunTable = legacy };
        }

        if (emptyTable)
        {
            throw new ReFsFormatException("Run table row without an embedded table.");
        }

        if (table.IsEmpty)
        {
            return new FileRecordStream { Name = string.Empty, Size = Info.Length, RunTable = table };
        }

        var payload = new EmbeddedTable(table).Payload;
        CheckSummary(payload);
        return new FileRecordStream { Name = string.Empty, Size = SummarySize(payload), RunTable = table, ParentVersion = parent };
    }

    /// <summary>
    ///     The run table of one version of the unnamed stream, and the version it is a delta of. The unnamed stream is a set of tables, one per version:
    ///     0x1000 is the live stream and each snapshot has its own id (0x1001 and up), stored in the snapshot's row. The key also names the version the
    ///     table was split from (u32 at 0x18; 8 on the oldest), and a version only has run rows for what changed since then: a lookup that finds no row
    ///     goes on to that version. The parent field and the fall-through are read from the images (a live stream unchanged since its last snapshot has
    ///     an empty table) and match forefst's extraction of every snapshot; how Windows itself interprets the key field is not established.
    /// </summary>
    public bool TryFindVersion(uint version, out ReadOnlySpan<byte> table, out uint parent)
    {
        foreach (var row in Rows)
        {
            if (Classify(row) == FileRowKind.UnnamedRunTable && row.Key.U32(RunTableVersionOffset) == version)
            {
                table = row.Value.IsEmpty ? throw new ReFsFormatException("Run table row without an embedded table.") : row.Value;
                parent = row.Key.U32(RunTableParentOffset);
                return true;
            }
        }

        table = default;
        parent = 0;
        return false;
    }

    /// <summary>The run table of the named stream with the given id; empty when there is none.</summary>
    private ReadOnlySpan<byte> FindRunTable(FileRowKind kind, ulong streamId)
    {
        ReadOnlySpan<byte> table = default;

        foreach (var row in Rows)
        {
            if (Classify(row) == kind && row.Key.U64(RunTableStreamIdOffset) == streamId)
                // An empty result means "no run table row", so a row without a table must not look like that.
            {
                table = row.Value.IsEmpty ? throw new ReFsFormatException("Run table row without an embedded table.") : row.Value;
            }
        }

        return table;
    }

    /// <summary>
    ///     Every key starts with {u32 length of the whole attribute, u32 offset of this row's part}. An inline stream longer than one row
    ///     (4,072 bytes on the images) is cut into chunk rows that differ only in the offset; in both layouts (ReFS 3.1 to 3.14 images).
    /// </summary>
    private const int ChunkOffsetOffset = 4;

    private const int ChunkPrefixLength = 8;

    private static bool IsContinuationChunk(ReadOnlySpan<byte> key) => key.U32(ChunkOffsetOffset) != 0;

    private static string LegacyStreamName(ReadOnlySpan<byte> key) => Encoding.Unicode.GetString(key[LegacyKeyHeaderLength..]);

    /// <summary>The whole attribute of an inline stream: the row's value, or the joined chunk rows when the key's length says there are more.</summary>
    private ReadOnlySpan<byte> AttributeValue(EntryView first, FileRowKind kind) => first.Key.U32(0) > (uint)first.Value.Length ? ReadChunkedAttribute(first.Key, kind) : first.Value;

    /// <summary>Joins the chunk rows of an inline stream. The chunks must be contiguous and add up to the length in their keys.</summary>
    private byte[] ReadChunkedAttribute(ReadOnlySpan<byte> firstKey, FileRowKind kind)
    {
        var total = firstKey.U32(0);
        var suffix = firstKey[ChunkPrefixLength..];

        if (total > MaximumChunkedAttributeLength)
        {
            throw new ReFsFormatException($"Inline stream attribute of {total} bytes is too long.");
        }

        var attribute = new byte[total];
        uint position = 0;

        foreach (var row in Rows)
        {
            var key = row.Key;

            if (Classify(row) != kind || !key[ChunkPrefixLength..].SequenceEqual(suffix))
            {
                continue;
            }

            if (key.U32(0) != total || key.U32(ChunkOffsetOffset) != position || row.Value.Length > total - position)
            {
                throw new ReFsFormatException("Inline stream attribute chunks are not contiguous.");
            }

            row.Value.CopyTo(attribute.AsSpan((int)position));
            position += (uint)row.Value.Length;
        }

        return position == total && total >= DataOffset
            ? attribute
            : throw new ReFsFormatException($"Inline stream attribute is incomplete ({position} of {total} bytes).");
    }

    /// <summary>A stream snapshot row: the attribute flag, and a value long enough for the version id and the time.</summary>
    private static bool IsSnapshot(ReadOnlySpan<byte> streamValue) =>
        ((StreamAttributeFlags)streamValue.U16(FlagsOffset)).HasFlag(StreamAttributeFlags.Snapshot) && streamValue.Length >= SnapshotTimeOffset + sizeof(ulong);

    private static string StreamName(ReadOnlySpan<byte> key) => key.Length == KeyHeaderLength ? string.Empty : Encoding.Unicode.GetString(key[KeyHeaderLength..]);

    private static bool IsEncrypted(ReadOnlySpan<byte> streamValue) => ((StreamAttributeFlags)streamValue.U16(FlagsOffset)).HasFlag(StreamAttributeFlags.Encrypted);

    private static bool IsResident(ReadOnlySpan<byte> streamValue) => !((StreamAttributeFlags)streamValue.U16(FlagsOffset)).HasFlag(StreamAttributeFlags.NonResident);

    /// <summary>
    ///     Rejects a stream summary that Windows treats as corrupt on open: a negative allocation or size, or a size above the allocation.
    ///     The summary is the block at 0x10 of a stream row's value
    ///     (allocation at +8, size at +0x10), or the payload of a run table.
    /// </summary>
    private static void CheckSummary(ReadOnlySpan<byte> summary)
    {
        if (summary.Length < SummarySizeOffset + sizeof(ulong))
        {
            return;
        }

        var allocated = (long)summary.U64(SummaryAllocatedOffset);
        var size = (long)summary.U64(SummarySizeOffset);

        if (allocated < 0 || size < 0 || size > allocated)
        {
            throw new ReFsFormatException($"Stream summary is inconsistent (size {size}, allocated {allocated}).");
        }
    }

    /// <summary>The size in a stream summary; the standard-information size when the summary is too short to hold one.</summary>
    private ulong SummarySize(ReadOnlySpan<byte> summary) => summary.Length < SummarySizeOffset + sizeof(ulong) ? Info.Length : summary.U64(SummarySizeOffset);

    private static ReadOnlySpan<byte> CheckedRunTable(ReadOnlySpan<byte> table)
    {
        if (!table.IsEmpty)
        {
            CheckSummary(new EmbeddedTable(table).Payload);
        }

        return table;
    }

    /// <summary>The inline data of a resident stream; throws when the record is not resident or its size does not fit the value.</summary>
    private static ReadOnlySpan<byte> ResidentData(ReadOnlySpan<byte> streamValue)
    {
        var size = streamValue.U64(SizeOffset);
        return !IsResident(streamValue) || size > (ulong)(streamValue.Length - DataOffset)
            ? throw new ReFsFormatException("Inconsistent resident stream record.")
            : streamValue.Slice(DataOffset, (int)size);
    }

    /// <summary>What a stream set's control row declares and what its tables show.</summary>
    private readonly record struct StreamSetCounts(bool HasControl, uint Tables, uint Linked, uint PresentTables, uint PresentLinked);
}