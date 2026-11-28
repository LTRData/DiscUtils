using DiscUtils.ReFs.BTree.Editing;
using DiscUtils.ReFs.BTree.Enums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Replay.Enums;

namespace DiscUtils.ReFs.Replay;

/// <summary>
///     Applies depth-3 redo records that target a file's stream run table (and its stream-set control row when snapshots or new tables are created).
///     Layouts:
///     StreamAllocate {v0 SRange {lcn u64, count u32}, v1 u32 flags (0x20 delayed, 0x02 valid, 0x40 replace, 0x400 encrypted, 0x10 checksums), v2 u64 vcn};
///     StreamFree {v0 Range {vcn, length} (two values) or {physical lcn, length} + v2 vcn (three values)};
///     SetRangeValid/Invalid {v0 Range {vcn, length}}; UpdateStreamSummary {v0 u64 size (also valid length), v1 u64 allocated bytes}.
///     <para>
///         ReFS 3.1 to 3.5 (legacy; from Server 2016 and 2019 lab images): the run table is the value of the attribute-0x80 row,
///         which does not carry the embedded-table flag. A new file gets that row from InsertRow with an 84-byte value and then CreateTable on this
///         path. The summary is not a record of its own: UpdateDataWithRoot writes v0 {allocated, size, valid length, 0} into the table's payload at
///         offset v1 (0x0C).
///     </para>
/// </summary>
internal static class RedoStreamReplay
{
    // The path kind of a run table changed with the version: 0x1E0 from ReFS 3.11, 0x1C0 on the ReFS 3.10 lab images
    // (same records and row layout as 0x1E0; only the schema id in a new table's header differs: 0x1A0 instead of 0x200), and on
    // lab images 0x110 (3.1) and 0x180 (3.4, 3.5).
    private const uint StreamTableKind = 0x1E0;
    private const uint StreamTableKind310 = 0x1C0;

    /// <summary>The table id and parent of a stream set's control row, and the parent of its oldest table.</summary>
    private const ulong StreamSetNoTable = 8;

    // Offsets in a stream table's 96-byte payload: the 40-byte summary starts at +0 (allocated @8, size @0x10, valid length @0x18).
    private const int SummaryAllocatedOffset = 8;
    private const int SummarySizeOffset = 0x10;
    private const int SummaryValidLengthOffset = 0x18;
    private const int UserPayloadOffset = 0x28;
    private static readonly uint[] LegacyStreamTableKinds = [0x110, 0x180];

    public static bool IsStreamTableKind(uint kind, out bool legacy)
    {
        if (kind is StreamTableKind or StreamTableKind310)
        {
            legacy = false;
            return true;
        }

        if (LegacyStreamTableKinds.Contains(kind))
        {
            legacy = true;
            return true;
        }

        legacy = false;
        return false;
    }

    public static void Apply(EmbeddedTableEditor fileRecord, RedoRecord record, bool legacy, EmbeddedRowReader? readBranchRows, List<StreamRunTable.Entry>? cloned)
    {
        var suffix = record.Path[2].Key.ToArray();
        var v = record.Values;

        // The stream table is its own row of the file record: key {u64 length of the value, the log's stream path key}, flags 1.
        // (Next to it sits a 0x28-byte placeholder row with a 40-byte value that is not touched.) The length prefix follows the value.
        var row = fileRecord.Rows.FirstOrDefault(r => r.Key.Length == suffix.Length + 8 && r.Key.AsSpan(8).SequenceEqual(suffix));

        if (record.Operation == RedoOperation.CreateTable)
        {
            if (!legacy && row is null && SplitStreamSetTable(fileRecord, suffix, readBranchRows))
            {
                return;
            }

            if (!legacy)
            {
                StoreStreamTable(fileRecord, row, suffix, EmbeddedTableEditor.CreateStreamTable(record.Path[2].Kind == StreamTableKind310 ? 0x1A0u : 0x200u).Build(), EntryProperties.EmbeddedTable);

                if (row is null)
                {
                    CountNewStreamSetTable(fileRecord, suffix);
                }
            }
            else if (row is null)
            {
                throw new NotSupportedException("the run table row does not exist");
            }
            else
            {
                StoreStreamTable(fileRecord, row, suffix, EmbeddedTableEditor.CreateLegacyStreamTable(row.Value, record.Path[2].Kind == 0x110 ? 0x120u : 0x1A0u).Build(), row.Flags);
            }

            return;
        }

        if (record.Operation is RedoOperation.DeleteTable or RedoOperation.DeleteTableAndRow)
        {
            if (row is not null)
            {
                fileRecord.Remove(row.Key); // the stream's run table is dropped (the file is truncated to nothing or deleted)
            }

            return;
        }

        if (row is null || !(legacy || row.Flags.HasFlag(EntryProperties.EmbeddedTable)))
        {
            throw new NotSupportedException("the stream table does not exist");
        }

        // A run table with many runs is a branch (its rows sit in pages of their own): it is rebuilt as one leaf, like a branch file record.
        var table = EmbeddedTableEditor.Parse(row.Value, readBranchRows);

        switch (record.Operation)
        {
            case RedoOperation.UpdateDataWithRoot when legacy && v.Count >= 2 && v[1].Length == 4
                                                       && v[1].Span.U32(0) + (ulong)v[0].Length <= (ulong)table.Payload.Length:
                v[0].Span.CopyTo(table.Payload.AsSpan((int)v[1].Span.U32(0)));
                break;
            // Logged right after CreateTable for a stream made on a volume with integrity streams (ReFS 3.4 and 3.5 lab images for a new file,
            // 3.14 for a new named stream), v0 = u64 1 there (CRC-32C). Stored run tables of integrity files differ from plain ones in the u16 at
            // +0x16 of the table header (1 against 0) on every version, the checksum kind this reader checks runs against.
            case RedoOperation.SetIntegrityInformation when v.Count == 1 && v[0].Length == 8 && v[0].Span.U64(0) <= ushort.MaxValue:
                table.SetChecksumKind((ushort)v[0].Span.U64(0));
                break;
            case RedoOperation.UpdateStreamUserPayload when v.Count >= 1 && UserPayloadOffset + v[0].Length <= table.Payload.Length:
                v[0].Span.CopyTo(table.Payload.AsSpan(UserPayloadOffset));
                break;
            case RedoOperation.UpdateStreamSummary:
                if (v.Count >= 1 && v[0].Length == 8)
                {
                    v[0].Span.CopyTo(table.Payload.AsSpan(SummarySizeOffset));
                    v[0].Span.CopyTo(table.Payload.AsSpan(SummaryValidLengthOffset));
                }

                if (v.Count >= 2 && v[1].Length == 8)
                {
                    v[1].Span.CopyTo(table.Payload.AsSpan(SummaryAllocatedOffset));
                }

                if (v.Count >= 5 && (FlagBits(v[3]) | FlagBits(v[4])) != 0)
                {
                    throw new NotSupportedException("stream summary flag changes are not supported");
                }

                break;
            case RedoOperation.StreamAllocate when v.Count >= 2 && v[0].Length == 12 && v[1].Length == 4:
                ApplyRuns(table, runs =>
                {
                    var flags = (StreamAllocateFlags)v[1].Span.U32(0);

                    if (flags.HasFlag(StreamAllocateFlags.PlaceholderChecksums))
                    {
                        throw new NotSupportedException("allocation flag 0x10 (a run created with match-all placeholder checksums) is not supported: no witnessed log holds it");
                    }

                    var run = (flags.HasFlag(StreamAllocateFlags.Delayed) ? EntryProperties.Delayed : EntryProperties.None)
                              | (flags.HasFlag(StreamAllocateFlags.Valid) ? EntryProperties.Valid : EntryProperties.None)
                              | (flags.HasFlag(StreamAllocateFlags.Encrypted) ? EntryProperties.Encrypted : EntryProperties.None);

                    var vcn = v.Count >= 3 && v[2].Length == 8 ? v[2].Span.U64(0) : 0;
                    runs.Add(vcn, v[0].Span.U32(8), v[0].Span.U64(0), run, flags.HasFlag(StreamAllocateFlags.ReplaceExisting));
                });

                break;
            case RedoOperation.StreamFree when v.Count >= 1 && v[0].Length == 16:
                ApplyRuns(table, runs => runs.Delete(v.Count >= 3 && v[2].Length == 8 ? v[2].Span.U64(0) : v[0].Span.U64(0), v[0].Span.U64(8)));
                break;
            case RedoOperation.ModifyStreamExtent when v.Count >= 5 && v[0].Length == 12 && v[3].Length > 0 && v[4].Length == 1 && v[4].Span[0] is 1 or 2 or 5:
                // ModifyStreamExtent: v0 {u64 vcn, u32 count}, v1 byte offset (always 0), v2 caller flags (not run flags), v3 the digests of
                // those clusters, v4 the checksum kind (1 = CRC32C, 2 = CRC-64, 5 = XXH64 with 8-byte digests, seen from Windows build 29683).
                // Without v3/v4 Windows sets a match-all checksum; that form has not been witnessed and is refused below.
                ApplyRuns(table, runs => runs.SetChecksums(v[0].Span.U64(0), v[0].Span.U32(8), v[3].Span));
                break;
            case RedoOperation.StreamPersistFastRunInsertion when v.Count >= 3 && v[0].Length == 12 && v[1].Length == 4 && v[2].Length == 12
                                                                  && (v.Count < 4 || v[3].Length == 0 || (v.Count >= 5 && v[3].Length == 1 && v[3].Span[0] is 1 or 2 or 5 && v[4].Length > 0)):
                // StreamPersistFastRunInsertion: v0 {lcn, count}, v1 flags (2 = valid), v2 {vcn, count}, v3 checksum kind (empty = none), v4
                // digests. It replaces whatever is in the range with the run, then converts it like ModifyStreamExtent.
                ApplyRuns(table, runs =>
                {
                    var run = ((StreamAllocateFlags)v[1].Span.U32(0)).HasFlag(StreamAllocateFlags.Valid) ? EntryProperties.Valid : EntryProperties.None;
                    var vcn = v[2].Span.U64(0);
                    var count = v[2].Span.U32(8);
                    runs.Add(vcn, count, v[0].Span.U64(0), run, true);

                    if (v.Count >= 5 && v[3].Length == 1)
                    {
                        runs.SetChecksums(vcn, count, v[4].Span);
                    }
                });

                break;
            case RedoOperation.DuplicateExtents when cloned is not null:
                // Block clone (v1 source cluster, v2 target cluster, v3 count; the source runs were read by the caller). The target range
                // then shares the source's clusters. Windows gives a checksummed target match-all placeholder digests; here the run keeps the
                // source's digests, which describe the same clusters, so the data stays verified.
                ApplyRuns(table, runs => runs.ReplaceWithClone(v[2].Span.U64(0), v[3].Span.U64(0), v[1].Span.U64(0), cloned));
                break;
            case RedoOperation.SetRangeValid or RedoOperation.SetRangeInvalid when v.Count >= 1 && v[0].Length == 16:
                ApplyRuns(table, runs => runs.SetValid(v[0].Span.U64(0), v[0].Span.U64(8), record.Operation == RedoOperation.SetRangeValid));
                break;
            default:
                throw new NotSupportedException("this stream operation is not supported");
        }

        StoreStreamTable(fileRecord, row, suffix, table.Build(), row.Flags);
    }

    /// <summary>
    ///     A stream snapshot (<c>refsutil streamsnapshot</c>, ReFS 3.10 lab image <c>w2022pre-dirtysnap</c>; derived from images) logs
    ///     <c>CreateTable</c> on the stream set key {..., u64 new id N, u64 P (the live id 0x1000), u32 flags, u32 0} and then writes the live stream
    ///     under {P, parent N}. The stored rows of a stream snapshotted before the checkpoint show what that means: the table that was live keeps its
    ///     runs and its parent X under the snapshot's id ({N, X}), and the live stream is a new table {P, N} with no runs, which reads fall through to
    ///     N. The new table takes the old one's header and stream summary (the live size is unchanged), and the set's control row counts one more
    ///     table and the next free id. Returns false when the record's table P does not exist (a CreateTable of a new stream).
    /// </summary>
    private static bool SplitStreamSetTable(EmbeddedTableEditor fileRecord, byte[] suffix, EmbeddedRowReader? readBranchRows)
    {
        const int SetTail = 24;

        if (suffix.Length < SetTail)
        {
            return false;
        }

        var tail = suffix.Length - SetTail;
        var newId = suffix.AsSpan().U64(tail);
        var liveId = suffix.AsSpan().U64(tail + 8);

        if (newId == liveId || newId == StreamSetNoTable || liveId == StreamSetNoTable)
        {
            return false;
        }

        var live = fileRecord.Rows.FirstOrDefault(r => r.IsEmbeddedTable && IsSetRow(r.Key, suffix, tail) && r.Key.AsSpan(8).U64(tail) == liveId);

        if (live is null)
        {
            return false;
        }

        var snapshotKey = (byte[])live.Key.Clone();
        BitConverter.TryWriteBytes(snapshotKey.AsSpan(8 + tail), newId);
        fileRecord.Remove(live.Key);
        fileRecord.Set(live with { Key = snapshotKey });

        var table = EmbeddedTableEditor.Parse(live.Value, readBranchRows);
        table.ReplaceRows([]);
        var value = table.Build();
        var liveKey = (byte[])live.Key.Clone();
        BitConverter.TryWriteBytes(liveKey.AsSpan(0), (ulong)value.Length);
        BitConverter.TryWriteBytes(liveKey.AsSpan(8 + tail + 8), newId);
        fileRecord.Set(new EmbeddedRow(live.Flags, liveKey, value));

        CountStreamSetTable(fileRecord, suffix, tail, newId);
        return true;
    }

    /// <summary>
    ///     A CreateTable of a new table in a stream set ({..., u64 id, u64 parent, u32 flags, u32 0}; the live stream of a new file is {0x1000, 8}):
    ///     the set's control row counts it. A new file's control row is logged as {next id 0x1000, 0 tables} before the CreateTable (lab images
    ///     <c>w2025-dirtynames</c> and <c>w2028pre-dirtynames</c>), and Windows stores {0x1001, 1} for a file with one live table.
    /// </summary>
    private static void CountNewStreamSetTable(EmbeddedTableEditor fileRecord, byte[] suffix)
    {
        const int SetTail = 24;

        if (suffix.Length < SetTail)
        {
            return;
        }

        var tail = suffix.Length - SetTail;
        var id = suffix.AsSpan().U64(tail);

        if (id != StreamSetNoTable && suffix.AsSpan().U64(tail + 8) != id)
        {
            CountStreamSetTable(fileRecord, suffix, tail, id);
        }
    }

    /// <summary>
    ///     The set's control row (key tail {8, 8, u32 1 (control), u32 0}, value {u64 next id, u32 table count, u32 linked count, u32 flags, ...})
    ///     counts one more table, and its next id passes <paramref name="id" />.
    /// </summary>
    private static void CountStreamSetTable(EmbeddedTableEditor fileRecord, byte[] suffix, int tail, ulong id)
    {
        var control = fileRecord.Rows.FirstOrDefault(r => !r.IsEmbeddedTable && IsSetRow(r.Key, suffix, tail)
                                                                             && r.Key.AsSpan(8).U64(tail) == StreamSetNoTable && r.Key.AsSpan(8).U64(tail + 8) == StreamSetNoTable
                                                                             && (r.Key.AsSpan(8).U32(tail + 16) & 1) != 0 && r.Value.Length >= 12);

        if (control is null)
        {
            return;
        }

        var counts = (byte[])control.Value.Clone();
        BitConverter.TryWriteBytes(counts.AsSpan(0), Math.Max(counts.AsSpan().U64(0), id + 1));
        BitConverter.TryWriteBytes(counts.AsSpan(8), counts.AsSpan().U32(8) + 1);
        fileRecord.Set(control with { Value = counts });
    }

    private static bool IsSetRow(byte[] key, byte[] suffix, int tail) => key.Length == suffix.Length + 8 && key.AsSpan(8, tail).SequenceEqual(suffix.AsSpan(0, tail));

    private static void StoreStreamTable(EmbeddedTableEditor fileRecord, EmbeddedRow? existing, byte[] suffix, byte[] table, EntryProperties flags)
    {
        var key = new byte[8 + suffix.Length];
        BitConverter.TryWriteBytes(key.AsSpan(0), (ulong)table.Length);
        suffix.CopyTo(key, 8);

        if (existing is not null)
        {
            fileRecord.Remove(existing.Key);
        }

        fileRecord.Set(new EmbeddedRow(flags, key, table));
    }

    private static int FlagBits(ReadOnlyMemory<byte> value) => value.Length >= 2 ? value.Span.U16(0) : 0;

    private static void ApplyRuns(EmbeddedTableEditor table, Action<StreamRunTable> change)
    {
        var runs = StreamRunTable.FromRows(table.Rows);
        change(runs);
        table.ReplaceRows(runs.ToRows());
    }
}