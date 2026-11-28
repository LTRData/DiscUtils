using System.Diagnostics.CodeAnalysis;
using DiscUtils.ReFs.BTree.Editing;
using DiscUtils.ReFs.BTree.Enums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries.Enums;
using DiscUtils.ReFs.Entries.Files;
using DiscUtils.ReFs.Replay.Enums;
using DiscUtils.ReFs.SystemTables;

namespace DiscUtils.ReFs.Replay;

/// <summary>
///     Row-level result of replaying the committed redo records that are newer than their table's last flush. Opt-in
///     (<see cref="ReFsFileSystemOptions.ReplayLog" />). Supported: InsertRow / DeleteRow / UpdateRow on top-level tables
///     (directory rows are full replacement values), UpdateDataWithRoot on a row's embedded table (replaces the standard
///     information payload, which carries size and times), and no-op markers. Records that change a row's embedded table
///     in other ways (stream allocation / free / summary, nested rows) mark the row incomplete. Anything else (creating or
///     deleting tables, container or schema changes) throws <see cref="NotSupportedException" /> rather than guessing.
/// </summary>
internal sealed class RedoOverlay
{
    private const uint EmbeddedRowKind = 0x130;

    private const int PayloadOffsetField = 4;

    private const ulong ObjectTableObjectId = 2;


    /// <summary>
    ///     Object LSNs the analysis pass takes from <c>TreeUpdateMarker</c> records older than the restart clock: the marker's LSN plus one.
    ///     The gate looks here before the object table.
    /// </summary>
    private readonly Dictionary<ulong, ulong> _analysedLsn = [];

    private readonly HashSet<ulong> _created = [];
    private readonly HashSet<ulong> _deleted = [];

    private readonly ILogger _logger;
    private readonly EmbeddedRowReader? _readBranchRows;
    private readonly Dictionary<ulong, TableOverlay> _tables = [];

    /// <summary>The checkpoint's restart clock (<see cref="Mount.Checkpoint.RestartClock" />); null when the caller has none (tests).</summary>
    private ulong? _restartClock;

    private RedoOverlay(ILogger logger, EmbeddedRowReader? readBranchRows = null)
    {
        _logger = logger;
        _readBranchRows = readBranchRows;
    }

    public static RedoOverlay Empty { get; } = new RedoOverlay(NullLogger.Instance);


    public bool IsEmpty => _tables.Count == 0 && _deleted.Count == 0;

    /// <summary>The object (a directory table) was created by a replayed record and has no stored table.</summary>
    public bool IsCreated(ulong objectId) => _created.Contains(objectId);

    /// <summary>The object (a directory table) was deleted by a replayed record.</summary>
    public bool IsDeleted(ulong objectId) => _deleted.Contains(objectId);

    public bool TryGetTable(ulong objectId, [NotNullWhen(true)] out TableOverlay? table) => _tables.TryGetValue(objectId, out table);

    /// <summary>
    ///     Applies the committed transactions of the scan in log order; <paramref name="transactions" /> stops after that many committed ones
    ///     (null: all), which is how a reader steps through the log. <paramref name="restartClock" /> is the checkpoint's restart clock; without it the
    ///     clock tests are left out (objects 1, 0xB and 0xE always replay and no marker gives an object an LSN).
    /// </summary>
    public static RedoOverlay Build(ILogger logger, RedoLogScan scan, ObjectTable objects, BaseRowReader readBase, EmbeddedRowReader? readBranchRows = null,
        int? transactions = null, ulong? restartClock = null)
    {
        var overlay = new RedoOverlay(logger, readBranchRows) { _restartClock = restartClock };
        var applied = 0;

        // Analysis pass: every TreeUpdateMarker whose clock (+0x20) is below the restart clock gives the object named by its value ({u64 0, u64
        // object id}) the LSN after its own, whether or not its transaction committed.
        if (restartClock is { } clock)
        {
            foreach (var transaction in scan.Transactions)
            {
                foreach (var record in transaction.Records)
                {
                    if (record.Operation == RedoOperation.TreeUpdateMarker && record.RestartClock < clock && record.Values.Count > 0 && record.Values[0].Length == 16 &&
                        record.Values[0].Span.U64(0) == 0)
                    {
                        overlay._analysedLsn.TryAdd(record.Values[0].Span.U64(8), record.EntryLsn + 1);
                    }
                }
            }
        }


        // A record that logs no path reuses the previous record's open table. After a ReparentTable that table is the moved one, so inherited records
        // follow it to its new key. This follows one table object; it is not an old-path alias.
        IReadOnlyList<RedoPathComponent>? movedTable = null;

        // A block whose first record is a TreeUpdateMarker (operation 18) is dropped whole, whatever follows it in the block, as Windows does.
        HashSet<(ulong EntryLsn, int Block)> droppedBlocks = [];
        HashSet<(ulong EntryLsn, int Block)> seenBlocks = [];

        foreach (var record in scan.Records)
        {
            if (seenBlocks.Add((record.EntryLsn, record.Block)) && record.Operation == RedoOperation.TreeUpdateMarker)
            {
                droppedBlocks.Add((record.EntryLsn, record.Block));
            }
        }

        foreach (var whole in scan.Transactions)
        {
            var transaction = droppedBlocks.Count switch
            {
                0 => whole,
                _ => whole with { Records = [.. whole.Records.Where(r => !droppedBlocks.Contains((r.EntryLsn, r.Block)))] }
            };

            if (transaction.Records.Count == 0)
            {
                continue;
            }

            if (!transaction.Committed)
            {
                Logs.ReplaySkip(logger, transaction.Records.Count, transaction.Records[0].EntryLsn);
                movedTable = null;
                continue;
            }

            if (applied++ == transactions)
            {
                break;
            }

            // Windows decides per transaction, on its first record (flag bit 0), whether it is already in the checkpoint. TreeUpdateMarker records
            // never reach that test, so the first other record decides.
            var first = transaction.Records.FirstOrDefault(r => r.Operation is not (RedoOperation.TreeUpdateMarker or RedoOperation.LogAdvanceMarker));

            if (first is null)
            {
                continue;
            }

            if (movedTable is not null && first.PathInherited)
            {
                first = first with { Path = movedTable };
            }

            if (!overlay.ShouldReplay(first, objects, out var reason))
            {
                Logs.ReplaySkippedTransaction(logger, transaction.Records.Count, first.EntryLsn, first.Operation, reason);
                continue;
            }

            foreach (var record in transaction.Records)
            {
                var effective = record;

                if (movedTable is not null && record.PathInherited)
                {
                    effective = record with { Path = movedTable };
                }
                else
                {
                    movedTable = null;
                }

                movedTable = overlay.Apply(effective, objects, readBase) ?? movedTable;
            }
        }

        return overlay;
    }

    /// <summary>
    ///     The replay test for a transaction, evaluated on its first record: object 2 (the object table) never, object 0x100 always; otherwise the
    ///     target object's flushed LSN decides: replay when the record's LSN is at or after it. An object with no LSN (in neither the checkpoint's object
    ///     table nor created by an earlier replayed transaction) is skipped unless the record creates it as a top-level table. Objects 1, 0xB and 0xE
    ///     replay when the record's clock (+0x20) is not older than the restart clock, and an object the analysis pass gave an LSN from a marker is
    ///     decided by that LSN before the object table.
    /// </summary>
    private bool ShouldReplay(RedoRecord first, ObjectTable objects, out string reason)
    {
        var objectId = RootObject(first);

        switch (objectId)
        {
            case ObjectTableObjectId:
                reason = "it starts on the object table";
                return false;
            case 0x100:
                reason = string.Empty;
                return true;
            case 1:
            case 0xB:
            case 0xE:
                // No LSN test for these: a record written before the restart clock is already in the checkpoint.
                reason = $"its record's clock 0x{first.RestartClock:X} is older than the restart clock 0x{_restartClock:X}";
                return _restartClock is not { } restart || first.RestartClock >= restart;
        }

        if (_analysedLsn.TryGetValue(objectId, out var analysedLsn))
        {
            reason = $"object 0x{objectId:X} has a tree-update marker before 0x{analysedLsn:X}";
            return first.EntryLsn >= analysedLsn;
        }

        if (_created.Contains(objectId) && !_deleted.Contains(objectId))
        {
            reason = string.Empty;
            return true;
        }

        if (!_deleted.Contains(objectId) && objects.TryGetLsn(objectId, out var tableLsn))
        {
            reason = $"object 0x{objectId:X} was flushed at 0x{tableLsn:X}";
            return first.EntryLsn >= tableLsn;
        }

        reason = $"object 0x{objectId:X} is not in the checkpoint";
        return first.Operation == RedoOperation.CreateTable && first.Path.Count == 1;
    }

    private static ulong RootObject(RedoRecord record)
    {
        if (record.Path.Count == 0 || !record.Path[0].IsRoot)
        {
            throw new NotSupportedException($"Redo record {record.Operation} at 0x{record.EntryLsn:X} has a table path this reader does not understand.");
        }

        return record.Path[0].ObjectId;
    }

    /// <summary>Applies one record; returns the new table path when it moved an embedded table (ReparentTable), else null.</summary>
    private IReadOnlyList<RedoPathComponent>? Apply(RedoRecord record, ObjectTable objects, BaseRowReader readBase)
    {
        if (record.Operation is RedoOperation.TreeUpdateMarker or RedoOperation.LogAdvanceMarker)
        {
            return null;
        }

        var objectId = RootObject(record);
        // Inside a transaction that passed the replay test, every record is applied (there is no per-record LSN test).
        var stored = objects.TryGetLsn(objectId, out _);

        if (!stored && !_created.Contains(objectId))
        {
            if (record.Path.Count == 1 && record.Operation == RedoOperation.CreateTable)
            {
                _created.Add(objectId); // a new table (a directory): its rows come from the records that follow
                Table(objectId);
                Logs.ReplayCreatedObject(_logger, objectId, record.EntryLsn);
                return null;
            }

            // Any other operation on a missing top-level table fails the redo in Windows (only CreateTable creates the table).
            throw new NotSupportedException($"Redo record {record.Operation} at 0x{record.EntryLsn:X} targets object 0x{objectId:X}, which neither exists nor is created by the log.");
        }


        if (record.Path.Count == 1)
        {
            ApplyTopLevel(record, objectId, readBase);
            return null;
        }

        if (record.Path[1].Kind == EmbeddedRowKind)
        {
            return ApplyEmbedded(record, objectId, objects, readBase);
        }

        throw new NotSupportedException($"Redo record {record.Operation} at 0x{record.EntryLsn:X} has an unsupported nested path.");
    }

    private void ApplyTopLevel(RedoRecord record, ulong objectId, BaseRowReader readBase)
    {
        switch (record.Operation)
        {
            case RedoOperation.InsertRow when record.Values.Count >= 2:
                Table(objectId).Set(record.Values[0].ToArray(), new OverlayRow(record.Values[1].ToArray(), null));
                break;
            case RedoOperation.UpdateRow when record.Values.Count >= 2:
                ApplyTopLevelUpdate(record, objectId, readBase);
                break;
            case RedoOperation.DeleteRow when record.Values.Count >= 1:
                Table(objectId).Set(record.Values[0].ToArray(), new OverlayRow(null, null));
                break;
            case RedoOperation.SetObjectRecordPayload:
            case RedoOperation.SetParentId:
            case RedoOperation.UnlinkParentObjectId:
                Logs.ReplayIgnoredBookkeeping(_logger, record.Operation, objectId);
                break;
            case RedoOperation.DeleteTable or RedoOperation.DeleteTableAndRow:
                _deleted.Add(objectId);
                _tables.Remove(objectId);
                Logs.ReplayDeletedObject(_logger, objectId, record.EntryLsn);
                break;
            case RedoOperation.CreateTable:
                Logs.ReplayIgnoredCreateTable(_logger, objectId);
                break;
            default:
                throw new NotSupportedException($"Redo operation {record.Operation} ({(uint)record.Operation}) on object 0x{objectId:X} at 0x{record.EntryLsn:X} is not supported.");
        }
    }

    /// <summary>
    ///     A top-level UpdateRow overwrites part of the stored row in place: v1 at the byte offset in v2 (a u32, 0 when absent), the row keeping its length.
    ///     Windows refuses an offset + length beyond the stored row, and copies the bytes at the offset. Without a stored row to patch
    ///     (the base read failed or the row is new to this reader) the value is kept whole, as before.
    /// </summary>
    private void ApplyTopLevelUpdate(RedoRecord record, ulong objectId, BaseRowReader readBase)
    {
        var key = ResolveNameKey(objectId, record.Values[0].ToArray(), readBase);
        var update = record.Values[1].ToArray();
        var offset = record.Values.Count >= 3 && record.Values[2].Length == 4 ? record.Values[2].Span.U32(0) : 0;

        var replayed = _tables.TryGetValue(objectId, out var table) && table.TryGet(key, out var row) ? row : null;
        var stored = replayed is not null ? replayed.Value : readBase(objectId, key);

        if (stored is null)
        {
            if (offset != 0)
            {
                throw new NotSupportedException($"Redo UpdateRow at 0x{record.EntryLsn:X} writes at offset {offset} of a row of object 0x{objectId:X} that does not exist.");
            }

            Table(objectId).Set(key, new OverlayRow(update, null));
            return;
        }

        if (offset + (ulong)update.Length > (ulong)stored.Length)
        {
            throw new NotSupportedException(
                $"Redo UpdateRow at 0x{record.EntryLsn:X} writes {update.Length} bytes at offset {offset} of a {stored.Length}-byte row of object 0x{objectId:X} (Windows refuses this).");
        }

        var value = (byte[])stored.Clone();
        update.CopyTo(value.AsSpan((int)offset));
        Table(objectId).Set(key, new OverlayRow(value, replayed?.Incomplete));
    }

    /// <summary>
    ///     ReFS 3.1 and 3.4 log UpdateRow on a directory's name row with entry kind 0 in the key ({u16 0x30, u16 0, name}; the lab's Server 2016 and 2019
    ///     images), while the stored row and the InsertRow / DeleteRow records carry the real kind (2 for a directory or link, 1 for a file). Windows
    ///     evidently finds the row by name alone. The kind-0 key is resolved here to the existing row of that name, replayed or stored; without one it is
    ///     kept as logged. Derived from images.
    /// </summary>
    private byte[] ResolveNameKey(ulong objectId, byte[] key, BaseRowReader readBase)
    {
        if (key.Length < 4 || key.AsSpan().U16(0) != 0x30 || key.AsSpan().U16(2) != 0)
        {
            return key;
        }

        foreach (var kind in (ReadOnlySpan<ushort>)[(ushort)ReFsEntryKind.Directory, (ushort)ReFsEntryKind.File])
        {
            var candidate = (byte[])key.Clone();
            BitConverter.TryWriteBytes(candidate.AsSpan(2), kind);
            var replayed = _tables.TryGetValue(objectId, out var table) && table.TryGet(candidate, out _);

            if (replayed || readBase(objectId, candidate) is not null)
            {
                return candidate;
            }
        }

        return key;
    }

    private IReadOnlyList<RedoPathComponent>? ApplyEmbedded(RedoRecord record, ulong objectId, ObjectTable objects, BaseRowReader readBase)
    {
        if (record.Path.Count == 2 && record.Operation == RedoOperation.DeleteTable)
        {
            // The embedded table of a row is deleted together with the row; the row's own DeleteRow record follows (or the row stays as is).
            Logs.ReplayIgnoredEmbeddedDelete(_logger, objectId, record.EntryLsn);
            return null;
        }

        if (record.Path.Count == 2 && record.Operation == RedoOperation.DeleteTableAndRow)
        {
            // Windows build 29683 logs no DeleteRow for the row that holds the deleted table: the row goes with it.
            Table(objectId).Set(record.Path[1].Key.ToArray(), new OverlayRow(null, null));
            Logs.ReplayDeletedObject(_logger, objectId, record.EntryLsn);
            return null;
        }

        var rowKey = record.Path[1].Key.ToArray();
        var table = Table(objectId);
        var current = table.TryGet(rowKey, out var existing) ? existing : null;

        if (record.Path.Count == 2 && record.Operation == RedoOperation.CreateTable && (current is { Value: null } || (current is null && readBase(objectId, rowKey) is null)))
        {
            // ReFS 3.1 to 3.5 create a file as an empty embedded table under its name key (also after the name was deleted in
            // this same log); its standard information (UpdateDataWithRoot) and its rows follow as records of their own.
            table.Set(rowKey, new OverlayRow(EmbeddedTableEditor.CreateFileRecord().Build(), null));
            Logs.ReplayCreatedRow(_logger, objectId, record.EntryLsn);
            return null;
        }

        if (current is { Value: null })
        {
            return null; // the row was deleted by a record in this same log: edits to it are moot
        }

        var value = current?.Value ?? readBase(objectId, rowKey);

        if (value is null)
        {
            Logs.ReplayMissingRow(_logger, record.Operation, objectId, record.EntryLsn);
            return null;
        }

        if (record.Path.Count == 2 && record.Operation == RedoOperation.UpdateDataWithRoot && record.Values.Count >= 1)
        {
            var offset = record.Values.Count >= 2 && record.Values[1].Length == 4 ? record.Values[1].Span.U32(0) : 0;
            table.Set(rowKey, new OverlayRow(ReplacePayload(value, record.Values[0].Span, offset), current?.Incomplete));
            return null;
        }

        if (record.Path.Count == 2 && record.Operation == RedoOperation.ReparentTable && record.Values.Count >= 2)
        {
            // Rename / move: the table (a file record) moves intact to the parent named by v0 under the key v1.
            var parent = ParentComponent(record.Values[0].Span, record);
            var destination = parent.ObjectId;

            if (!objects.TryGetLsn(destination, out _))
            {
                throw new NotSupportedException($"Redo record ReparentTable at 0x{record.EntryLsn:X} moves a row into object 0x{destination:X}, which is not in the object table.");
            }

            var newKey = record.Values[1].ToArray();
            table.Set(rowKey, new OverlayRow(null, null));
            Table(destination).Set(newKey, new OverlayRow(value, current?.Incomplete));
            return [parent, record.Path[1] with { Key = newKey }];
        }

        if (current?.Incomplete is not null)
        {
            return null; // an earlier change to this row could not be applied: later edits would act on a stale table
        }

        try
        {
            // A block clone names another stream: its runs are read here, where the other tables can be reached. On a file-row path
            // (two components) there is no target stream; ApplyNested refuses it as an operation on a file record.
            var cloned = record.Operation == RedoOperation.DuplicateExtents && record.Path.Count >= 3 ? ClonedRuns(record, readBase) : null;
            table.Set(rowKey, new OverlayRow(ApplyNested(value, record, cloned), null));
        }
        catch (NotSupportedException ex)
        {
            table.Set(rowKey, new OverlayRow(value, $"{record.Operation}: {ex.Message}"));
        }

        return null;
    }

    /// <summary>Applies a record that edits the embedded table of a file row (depth 2) or of one of its stream tables (depth 3).</summary>
    private byte[] ApplyNested(byte[] value, RedoRecord record, List<StreamRunTable.Entry>? cloned)
    {
        var editor = EmbeddedTableEditor.Parse(value, _readBranchRows);

        if (record.Path.Count == 2)
        {
            ApplyFileRowOperation(editor, record);
        }
        else if (record.Path.Count == 3 && RedoStreamReplay.IsStreamTableKind(record.Path[2].Kind, out var legacy))
        {
            RedoStreamReplay.Apply(editor, record, legacy, _readBranchRows, cloned);
        }
        else
        {
            throw new NotSupportedException("unsupported table path");
        }

        return editor.Build();
    }

    /// <summary>
    ///     The source runs of a DuplicateExtents record (block clone, FSCTL_DUPLICATE_EXTENTS_TO_FILE; ReFS 3.14 lab image):
    ///     v0 = the source stream's 3-component table path (directory object, file's name key, stream's key),
    ///     v1 u64 source cluster, v2 u64 target cluster, v3 u64 cluster count. The source is read as the log has left it so far.
    /// </summary>
    private List<StreamRunTable.Entry> ClonedRuns(RedoRecord record, BaseRowReader readBase)
    {
        var v = record.Values;

        if (v.Count != 4 || v[1].Length != 8 || v[2].Length != 8 || v[3].Length != 8)
        {
            throw new NotSupportedException("this form of the block clone record is not understood");
        }

        if (!RedoPathComponent.TryParsePath(v[0].Span, 3, out var path)
            || !path[0].IsRoot
            || path[2].Kind != record.Path[2].Kind)
        {
            throw new NotSupportedException("the source path of the block clone is not understood");
        }

        var sourceObject = path[0].ObjectId;
        var nameKey = path[1].Key.ToArray();
        var streamKey = path[2].Key;
        byte[]? value;

        if (_tables.TryGetValue(sourceObject, out var sourceTable) && sourceTable.TryGet(nameKey, out var changed))
        {
            if (changed!.Incomplete is not null)
            {
                throw new NotSupportedException("the source of the block clone could not be replayed itself");
            }

            value = changed.Value;
        }
        else
        {
            value = readBase(sourceObject, nameKey);
        }

        if (value is null)
        {
            throw new NotSupportedException("the source file of the block clone was not found");
        }

        var file = EmbeddedTableEditor.Parse(value, _readBranchRows);
        var row = file.Rows.FirstOrDefault(r => r.Key.Length == streamKey.Length + 8 && r.Key.AsSpan(8).SequenceEqual(streamKey.Span))
                  ?? throw new NotSupportedException("the source stream of the block clone has no run table");

        return StreamRunTable.FromRows(EmbeddedTableEditor.Parse(row.Value, _readBranchRows).Rows).Slice(v[1].Span.U64(0), v[3].Span.U64(0));
    }

    private static void ApplyFileRowOperation(EmbeddedTableEditor editor, RedoRecord record)
    {
        var v = record.Values;

        switch (record.Operation)
        {
            // v3 = the row's flags. Bit 0x04 is set on many inserts in the ReFS 3.14 lab logs (v3 = 4 or 5) but never on the same
            // rows on disk (0 or 1 there), so it is a marker of the log and is not stored. Image-derived.
            case RedoOperation.InsertRow when v.Count >= 2:
                editor.Set(new EmbeddedRow(v.Count >= 4 && v[3].Length >= 2 ? (EntryProperties)v[3].Span.U16(0) & ~EntryProperties.LogOnly : EntryProperties.None, v[0].ToArray(), v[1].ToArray()));
                break;
            case RedoOperation.UpdateRow when v.Count >= 3 && v[2].Length == 4:
            {
                // v2 = a byte offset into the row's value: v1 overwrites that part only (a named-stream row of a new stream on the
                // ReFS 3.14 lab image: 40 bytes at offset 0x10 of 116; the flushed image holds exactly that). Image-derived.
                var key = v[0].ToArray();
                var offset = v[2].Span.U32(0);

                if (!editor.TryFind(key, out var existing) || offset + (ulong)v[1].Length > (ulong)existing.Value.Length)
                {
                    throw new NotSupportedException("a partial row update outside the row is not supported");
                }

                var value = (byte[])existing.Value.Clone();
                v[1].Span.CopyTo(value.AsSpan((int)offset));
                editor.Set(existing with { Value = value });
                break;
            }
            case RedoOperation.UpdateRow when v.Count >= 2:
            {
                var key = v[0].ToArray();
                editor.Set(new EmbeddedRow(editor.TryFind(key, out var existing) ? existing.Flags : EntryProperties.None, key, v[1].ToArray()));
                break;
            }
            case RedoOperation.DeleteRow when v.Count >= 1:
                editor.Remove(v[0].ToArray());
                break;
            default:
                throw new NotSupportedException("this operation on a file record is not supported");
        }
    }

    /// <summary>The single root component of the table path in a ReparentTable value.</summary>
    private static RedoPathComponent ParentComponent(ReadOnlySpan<byte> path, RedoRecord record)
    {
        if (RedoPathComponent.TryParsePath(path, 1, out var components) && components[0].IsRoot)
        {
            return components[0];
        }

        throw new NotSupportedException($"Redo record ReparentTable at 0x{record.EntryLsn:X} names a destination this reader does not understand.");
    }


    /// <summary>The payload (standard information) of an embedded-table value is [payload offset, node offset); the record replaces its first bytes.</summary>
    private static byte[] ReplacePayload(byte[] value, ReadOnlySpan<byte> payload, uint offset)
    {
        var table = new EmbeddedTable(value);
        int payloadOffset = value.AsSpan().U16(PayloadOffsetField);

        if (offset + (ulong)payload.Length > (ulong)(table.NodeOffset - payloadOffset))
        {
            throw new NotSupportedException("A redo payload update is larger than the row's payload area.");
        }

        var patched = (byte[])value.Clone();
        payload.CopyTo(patched.AsSpan(payloadOffset + (int)offset));
        return patched;
    }

    private TableOverlay Table(ulong objectId)
    {
        if (!_tables.TryGetValue(objectId, out var table))
        {
            _tables[objectId] = table = new TableOverlay();
        }

        return table;
    }
}