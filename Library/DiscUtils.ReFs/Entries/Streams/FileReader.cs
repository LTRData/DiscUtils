using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.BTree.Enums;
using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries.Enums;
using DiscUtils.ReFs.Entries.Files;
using DiscUtils.ReFs.IO;
using DiscUtils.ReFs.SystemTables;

namespace DiscUtils.ReFs.Entries.Streams;

/// <summary>
///     Opens the data streams of a file. <see cref="FileRecord" /> finds the stream in the file's record: its inline data, or the
///     embedded table of its runs, which this class turns into a <see cref="RunFileStream" /> (inline data becomes a <see cref="ResidentFileStream" />).
/// </summary>
internal sealed class FileReader(
    IBlockSource volume,
    PageReader pages,
    TreeReader tree,
    StreamChecksumPolicy? policy,
    bool verifyData,
    ILogger logger,
    Lazy<UpcaseTable>? upcase = null)
{
    private const int RunTableChecksumKindOffset = 0x16;

    public Stream Open(ReFsEntry entry, string? streamName)
    {
        if (entry.IsDirectory)
        {
            throw new IOException($"'{entry.Name}' is a directory.");
        }

        var location = entry.Location
                       ?? throw new InvalidOperationException("The entry was not produced by a volume enumeration.");

        if (location.IncompleteReason is { } reason)
        {
            throw new NotSupportedException($"The data of this file cannot be read: redo-log replay did not apply every change to it ({reason}).");
        }

        var record = new FileRecord(tree, location.ReadValue(pages));

        if (!record.TryFindStream(streamName, out var stream, upcase?.Value))
        {
            throw new FileNotFoundException($"Stream '{streamName}' of '{entry.Name}' was not found.", streamName);
        }

        // EFS (lab image w2025-efs): the data of every stream is ciphertext, its runs carry EntryProperties.Encrypted, and the key material is a
        // hidden "$EFS" stream. An empty stream has nothing to decrypt and opens as usual.
        // A stream whose row has StreamAttributeFlags.Encrypted is decrypted by Windows on read even without the attribute. The file's own standard
        // information is checked too: Windows keeps the file's attributes there whole and sets Encrypted with the stream bit, while it updates only
        // the opened link's directory row, so another hard link's row can lag.
        if ((entry.Attributes.HasFlag(ReFsAttributes.Encrypted) || record.Info.Attributes.HasFlag(ReFsAttributes.Encrypted) || stream.IsEncrypted) && stream.Size != 0)
        {
            throw EncryptedData(entry, stream.Name);
        }

        if (stream.IsResident)
        {
            Logs.ResidentOpened(logger, stream.Name.Length == 0 ? entry.Name : $"{entry.Name}:{stream.Name}", (ulong)stream.ResidentData.Length);
            return new ResidentFileStream(stream.ResidentData.ToArray());
        }

        var runs = stream.RunTable.IsEmpty ? [] : ReadRuns(stream.RunTable);

        if (stream.RunTable.IsEmpty && stream.Size != 0)
        {
            throw new NotSupportedException($"Nonempty stream '{stream.Name}' of '{entry.Name}' has no allocation table; its data mapping cannot be established.");
        }

        // A version of the unnamed stream only has rows for what changed since the version it was split from: the rest comes from that one, and so on.
        HashSet<uint> seen = [];
        var parent = stream.ParentVersion;

        while (parent is { } version && version != 8)
        {
            if (!seen.Add(version))
            {
                throw new ReFsFormatException($"Stream '{stream.Name}' of '{entry.Name}' has cyclic snapshot ancestry at version 0x{version:X}.");
            }

            if (!record.TryFindVersion(version, out var table, out var next))
            {
                throw new NotSupportedException($"Stream '{stream.Name}' of '{entry.Name}' refers to unavailable snapshot version 0x{version:X}; its data mapping is incomplete.");
            }

            runs = Underlay(runs, ReadRuns(table));
            parent = next;
        }

        runs.RemoveAll(run => run.IsDelayed || run.IsTombstone); // Drop holes only after they have masked older allocations.

        if (runs.Exists(run => run.Flags.HasFlag(EntryProperties.Encrypted)))
        {
            throw EncryptedData(entry, stream.Name);
        }

        return OpenRuns(entry, runs, stream.Size);
    }

    private static ReFsEncryptedDataException EncryptedData(ReFsEntry entry, string streamName)
    {
        var name = streamName.Length == 0 ? entry.Name : $"{entry.Name}:{streamName}";
        return new ReFsEncryptedDataException($"'{name}' is EFS-encrypted: its data on disk is ciphertext, and this reader does not decrypt it.");
    }

    /// <summary>Adds the parts of <paramref name="lower" /> (an older version's runs) that no run of <paramref name="upper" /> covers.</summary>
    private static List<Run> Underlay(List<Run> upper, List<Run> lower)
    {
        if (lower.Count == 0)
        {
            return upper;
        }

        List<Run> covered = [.. upper.OrderBy(run => run.VirtualCluster)];
        List<Run> result = [.. covered];
        var first = 0;

        foreach (var run in lower)
        {
            var start = run.VirtualCluster;

            while (first < covered.Count && covered[first].EndVirtualCluster <= start)
            {
                first++;
            }

            for (var i = first; i < covered.Count; i++)
            {
                var above = covered[i];

                if (above.EndVirtualCluster <= start)
                {
                    continue;
                }

                if (above.VirtualCluster >= run.EndVirtualCluster)
                {
                    break;
                }

                if (above.VirtualCluster > start)
                {
                    result.Add(Clip(run, start, above.VirtualCluster));
                }

                start = Math.Max(start, above.EndVirtualCluster);
            }

            if (start < run.EndVirtualCluster)
            {
                result.Add(Clip(run, start, run.EndVirtualCluster));
            }
        }

        return result;
    }

    /// <summary>A visible slice, retaining the original allocation as the checksum verification domain.</summary>
    private static Run Clip(Run run, ulong start, ulong end)
    {
        if (start == run.VirtualCluster && end == run.EndVirtualCluster)
        {
            return run;
        }

        var skipped = start - run.VirtualCluster;
        var original = run.IntegrityRange ?? new RunIntegrityRange(run.Lcn, run.Count, 0);
        return new Run(start, (uint)(end - start), run.Lcn + skipped, run.Flags, run.Checksums)
        {
            IntegrityRange = run.HasChecksums || run.Checksums is not null
                ? original with { SkippedClusters = original.SkippedClusters + skipped }
                : null
        };
    }

    private RunFileStream OpenRuns(ReFsEntry entry, List<Run> runs, ulong length)
    {
        // Guard kept: the arguments (a LINQ sum and count over the runs) are computed before the generated method can check the level.
        if (logger.IsEnabled(LogLevel.Debug))
        {
            Logs.FileOpened(logger, entry.Name, entry.FileId, length, runs.Count, runs.Sum(r => r.Count), runs.Count(r => r.Checksums is not null),
                entry.Replay == ReFsReplayState.None ? string.Empty : " (changed by the replayed redo log)");

            // Guard kept: it also skips the loop over every run.
            if (logger.IsEnabled(LogLevel.Trace))
            {
                for (var i = 0; i < runs.Count; i++)
                {
                    Logs.RunListed(logger, i, runs[i].VirtualCluster, runs[i].Count, runs[i].Lcn, runs[i].Flags);
                }
            }
        }

        return new RunFileStream(volume, pages.Map, pages.ClusterSize, runs, length, policy, verifyData, logger, entry.Name);
    }

    private List<Run> ReadRuns(ReadOnlySpan<byte> embeddedTableValue)
    {
        List<Run> runs = [];
        var table = new EmbeddedTable(embeddedTableValue);

        foreach (var row in tree.Rows(embeddedTableValue, table.NodeOffset))
            // Run rows are compact entries on disk; rows the redo log inserted are normal entries (key {vcn, count}, value = the 24-byte run header).
        {
            if (row.IsCompact || (row.Key.Length == 12 && row.Value.Length >= 24))
                // Runs without clusters (IsDelayed) are kept here: in a version's table they hide the older version's runs. The caller drops them.
            {
                runs.Add(new Run(row.Key, row.Value));
            }
        }

        // Windows derives the checksum kind from the volume, not from the table. The run table header also records
        // it (u16 @0x16: 0 none, 1 CRC32C, 2 CRC-64, inferred 4 SHA256); disagreement is unsupported.
        if (runs.Any(run => run.Checksums is not null))
        {
            var recorded = embeddedTableValue.Length >= RunTableChecksumKindOffset + sizeof(ushort)
                ? (ChecksumKind)embeddedTableValue.U16(RunTableChecksumKindOffset)
                : ChecksumKind.None;

            if (policy is { } expected && recorded != expected.Kind)
            {
                throw new NotSupportedException($"Run table records checksum kind {(byte)recorded}, but the volume rule gives {(byte)expected.Kind}.");
            }
        }

        runs.Sort((left, right) => left.VirtualCluster.CompareTo(right.VirtualCluster));
        ulong end = 0;

        foreach (var run in runs)
        {
            if (run.Count == 0 || run.EndVirtualCluster < run.VirtualCluster || run.VirtualCluster < end)
            {
                throw new ReFsFormatException("Allocation table contains empty, overlapping or overflowing run ranges.");
            }

            end = run.EndVirtualCluster;
        }

        return runs;
    }
}