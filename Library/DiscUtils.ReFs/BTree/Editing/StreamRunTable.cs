using DiscUtils.ReFs.BTree.Enums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Replay.Enums;

namespace DiscUtils.ReFs.BTree.Editing;

/// <summary>
///     Replay-time model of a stream's run table (the rows of a non-resident stream's embedded table). A row is key {u64 virtual cluster, u32 count} and
///     a 24-byte run header {u64 lcn, u16 flags, u16 size (0x18 + checksum bytes), u64 vcn, u32 count}: as a normal entry (value = the header) or, when
///     <see cref="EntryProperties.Compact" /> is set, as a compact entry (the header itself). Runs of integrity streams (
///     <see
///         cref="EntryProperties.Checksums" />
///     ) carry their checksum bytes after the header, an equal share per cluster. The checksum update
///     (<see cref="RedoOperation.ModifyStreamExtent" />) is modeled from the records seen on real logs (v0 {vcn, count}, v3 the digests of those clusters).
/// </summary>
internal sealed class StreamRunTable
{
    private const int HeaderLength = 24;

    private readonly List<Entry> _runs;

    private StreamRunTable(List<Entry> runs)
    {
        _runs = runs;
    }

    public IReadOnlyList<Entry> Runs => _runs;

    public static StreamRunTable FromRows(IEnumerable<EmbeddedRow> rows)
    {
        List<Entry> runs = [];

        foreach (var row in rows)
        {
            if (row.Key.Length != 12 || row.Value.Length < HeaderLength)
            {
                throw new NotSupportedException("The stream table holds a row that is not a run.");
            }

            ReadOnlySpan<byte> value = row.Value;
            var flags = (EntryProperties)value.U16(8);

            if (flags.HasFlag(EntryProperties.OtherChecksums))
            {
                throw new NotSupportedException($"Editing a stream table with run flags 0x{flags:X} is not supported.");
            }

            var checksums = flags.HasFlag(EntryProperties.Checksums) && value.Length > HeaderLength ? value[HeaderLength..].ToArray() : null;

            if (checksums is not null && checksums.Length % row.Key.AsSpan().U32(8) != 0)
            {
                throw new NotSupportedException("A run's checksum bytes do not divide evenly over its clusters.");
            }

            runs.Add(new Entry(row.Key.AsSpan().U64(0), row.Key.AsSpan().U32(8), value.U64(0), flags, checksums));
        }

        runs.Sort((a, b) => a.Vcn.CompareTo(b.Vcn));
        return new StreamRunTable(runs);
    }

    public static StreamRunTable Empty() => new StreamRunTable([]);

    /// <summary>Removes the virtual range [start, start + count): fully covered runs go, boundary runs are trimmed or split.</summary>
    public void Delete(ulong start, ulong count)
    {
        var end = start + count;
        List<Entry> result = [];

        foreach (var run in _runs)
        {
            var runEnd = run.Vcn + run.Count;

            if (runEnd <= start || run.Vcn >= end)
            {
                result.Add(run);
                continue;
            }

            if (run.Vcn < start)
            {
                result.Add(Piece(run, run.Vcn, start));
            }

            if (runEnd > end)
            {
                result.Add(Piece(run, end, runEnd));
            }
        }

        _runs.Clear();
        _runs.AddRange(result);
    }

    /// <summary>Adds a run (replacing anything in its range when <paramref name="replace" /> is set).</summary>
    public void Add(ulong vcn, uint count, ulong rawLcn, EntryProperties flags, bool replace)
    {
        if (replace)
        {
            Delete(vcn, count);
        }
        else if (_runs.Any(r => r.Vcn < vcn + count && vcn < r.Vcn + r.Count))
        {
            throw new NotSupportedException($"Allocation of clusters {vcn}..{vcn + count - 1} overlaps an existing run.");
        }

        _runs.Add(new Entry(vcn, count, rawLcn, flags));
        _runs.Sort((a, b) => a.Vcn.CompareTo(b.Vcn));
    }

    /// <summary>Marks the range valid (data written) or invalid, slicing runs at the edges (<c>SetRangeValidState</c>).</summary>
    public void SetValid(ulong start, ulong count, bool valid)
    {
        var end = start + count;
        List<Entry> result = [];

        foreach (var run in _runs)
        {
            var runEnd = run.Vcn + run.Count;

            if (runEnd <= start || run.Vcn >= end)
            {
                result.Add(run);
                continue;
            }

            var from = Math.Max(run.Vcn, start);
            var to = Math.Min(runEnd, end);

            if (from > run.Vcn)
            {
                result.Add(Piece(run, run.Vcn, from));
            }

            var middle = Piece(run, from, to);
            result.Add(middle with { Flags = valid ? middle.Flags | EntryProperties.Valid : middle.Flags & ~EntryProperties.Valid });

            if (runEnd > to)
            {
                result.Add(Piece(run, to, runEnd));
            }
        }

        _runs.Clear();
        _runs.AddRange(result);
    }

    /// <summary>
    ///     <c>ModifyStreamExtent</c>: stores the digests of the clusters [start, start + count) in the runs that cover them. The covered part of each
    ///     run is converted: it gets <see cref="EntryProperties.Checksums" /> and the valid flag (<see cref="EntryProperties.Valid" />) and its checksum
    ///     bytes. A run that already has checksums is left untouched (replay is idempotent) and delayed or encrypted runs are refused, as in Windows.
    ///     A range that covers only part of a run splits it (see below).
    /// </summary>
    public void SetChecksums(ulong start, uint count, ReadOnlySpan<byte> digests)
    {
        if (count == 0 || digests.Length % count != 0)
        {
            throw new NotSupportedException("The checksum bytes of a stream extent do not divide evenly over its clusters.");
        }

        var perCluster = digests.Length / (int)count;
        var end = start + count;
        ulong covered = 0;
        List<Entry> result = [];

        foreach (var run in _runs)
        {
            var runEnd = run.Vcn + run.Count;

            if (runEnd <= start || run.Vcn >= end)
            {
                result.Add(run);
                continue;
            }

            var from = Math.Max(run.Vcn, start);
            var to = Math.Min(runEnd, end);
            covered += to - from;

            if ((run.Flags & (EntryProperties.Delayed | EntryProperties.Encrypted)) != EntryProperties.None)
            {
                throw new NotSupportedException($"Checksums for the delayed or encrypted run at cluster {run.Vcn} cannot be set (Windows refuses this too).");
            }

            if (run.Flags.HasFlag(EntryProperties.Checksums))
            {
                result.Add(run);
                continue;
            }

            // The log of a real flush names parts of a run (a 30-cluster append is logged as 1 + 15 + 15 clusters), so the run is split
            // at the edges of the range and only the covered part is converted.
            if (from > run.Vcn)
            {
                result.Add(Piece(run, run.Vcn, from));
            }

            result.Add(Piece(run, from, to) with
            {
                Flags = run.Flags | EntryProperties.Checksums | EntryProperties.Valid,
                Checksums = digests.Slice((int)(from - start) * perCluster, (int)(to - from) * perCluster).ToArray()
            });

            if (runEnd > to)
            {
                result.Add(Piece(run, to, runEnd));
            }
        }

        if (covered != count)
        {
            throw new NotSupportedException($"Checksums for clusters {start}..{end - 1} name clusters that are not allocated.");
        }

        _runs.Clear();
        _runs.AddRange(result);
    }

    /// <summary>The parts of the runs that lie in [start, start + count), cut at its edges (with their share of the checksum bytes).</summary>
    public List<Entry> Slice(ulong start, ulong count)
    {
        var end = start + count;
        List<Entry> result = [];

        foreach (var run in _runs)
        {
            var runEnd = run.Vcn + run.Count;

            if (runEnd > start && run.Vcn < end)
            {
                result.Add(Piece(run, Math.Max(run.Vcn, start), Math.Min(runEnd, end)));
            }
        }

        return result;
    }

    /// <summary>
    ///     A block clone: the range [target, target + count) is replaced by <paramref name="source" /> (runs of another stream cut from
    ///     [sourceStart, sourceStart + count)), which keep their clusters, flags and checksum bytes. Parts the source has no run for become holes.
    /// </summary>
    public void ReplaceWithClone(ulong target, ulong count, ulong sourceStart, IEnumerable<Entry> source)
    {
        Delete(target, count);

        foreach (var run in source)
        {
            _runs.Add(run with { Vcn = run.Vcn - sourceStart + target });
        }

        _runs.Sort((a, b) => a.Vcn.CompareTo(b.Vcn));
    }

    /// <summary>The rows of the table, in key order.</summary>
    public List<EmbeddedRow> ToRows() => [.. _runs.Select(ToRow)];

    /// <summary>The part [from, to) of a run: a later start moves the lcn on and drops the leading checksum bytes.</summary>
    private static Entry Piece(Entry run, ulong from, ulong to)
    {
        var skip = from - run.Vcn;
        byte[]? checksums = null;

        if (run.Checksums is { } all)
        {
            var perCluster = all.Length / (int)run.Count;
            checksums = all.AsSpan((int)skip * perCluster, (int)(to - from) * perCluster).ToArray();
        }

        // Delayed runs keep lcn = vcn, physical runs move on by the same amount.
        return run with { Vcn = from, Count = (uint)(to - from), RawLcn = run.RawLcn + skip, Checksums = checksums };
    }

    private static EmbeddedRow ToRow(Entry run)
    {
        var key = new byte[12];
        BitConverter.TryWriteBytes(key.AsSpan(0), run.Vcn);
        BitConverter.TryWriteBytes(key.AsSpan(8), run.Count);

        var checksumLength = run.Flags.HasFlag(EntryProperties.Checksums) ? run.Checksums?.Length ?? 0 : 0;
        var size = HeaderLength + checksumLength; // exact: the size field is not padded (the node pads between entries)
        var entry = new byte[size];
        BitConverter.TryWriteBytes(entry.AsSpan(0), run.RawLcn);
        BitConverter.TryWriteBytes(entry.AsSpan(8), (ushort)run.Flags);
        BitConverter.TryWriteBytes(entry.AsSpan(10), (ushort)size);
        key.CopyTo(entry, 12);
        run.Checksums?.CopyTo(entry, HeaderLength);

        // Compact entries carry the whole entry; other runs are normal entries whose value is the entry.
        return new EmbeddedRow(run.Flags.HasFlag(EntryProperties.Compact) ? run.Flags : EntryProperties.None, key, entry);
    }

    internal sealed record Entry(ulong Vcn, uint Count, ulong RawLcn, EntryProperties Flags, byte[]? Checksums = null);
}