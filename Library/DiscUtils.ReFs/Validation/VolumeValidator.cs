using System.Diagnostics;
using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries;
using DiscUtils.ReFs.Entries.Directories;
using DiscUtils.ReFs.Entries.Enums;
using DiscUtils.ReFs.Mount;
using DiscUtils.ReFs.Replay;
using DiscUtils.ReFs.SystemTables;

namespace DiscUtils.ReFs.Validation;

/// <summary>
///     Walks everything the reader understands and records what cannot be read or verified: every checkpoint root and every
///     object's tree (page checksum verification follows the open options), the upcase table and security row envelopes, the directory tree
///     (listing, extended attributes, security references) and, optionally, all file data (integrity-stream checksums,
///     decompression). Problems do not stop the walk; each is recorded with its location.
/// </summary>
internal static class VolumeValidator
{
    private const int BufferSize = 1 << 20;
    private const ulong StreamProgressBytes = 16 << 20; // a large stream reports progress every 16 MiB

    public static ValidationReport Run(ReFsVolume volume, bool verifyFileData, IProgress<ValidationProgress>? progress, IStreamOpener? opener,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch watch = Stopwatch.StartNew();
        long initialPageReads = volume.Tree.Pages.PagesRead;
        ValidationReport report = new ValidationReport(volume.Logger)
        {
            MetadataChecksumsEnabled = volume.Tree.Pages.VerifyChecksums,
            DataChecksumsEnabled = verifyFileData && volume.VerifyDataChecksums
        };

        Logs.ValidationStarted(volume.Logger, verifyFileData);
        ProgressSink sink = new ProgressSink(progress, report);
        sink.Notify(string.Empty);

        if (!report.MetadataChecksumsEnabled)
        {
            report.AddWarning("Metadata checksum verification is disabled.");
        }

        if (verifyFileData && !report.DataChecksumsEnabled)
        {
            report.AddWarning("File data checksum verification is disabled.");
        }

        if (volume.Info.LastShutdownWasDirty)
        {
            string logState = DescribeLog(volume);
            report.AddWarning(
                $"Shutdown state is unknown (unverified payload heuristic suggests dirty): the redo log is {(volume.ReplayEnabled ? "processed for replay (experimental)" : "not replayed")}{logState}, so recent changes may be missing or stale.");
        }
        else
        {
            report.AddWarning("Shutdown state is unknown; the legacy payload heuristic suggests clean, but user-payload presence does not establish a clean shutdown.");
        }

        long phasePages = volume.Tree.Pages.PagesRead;
        TimeSpan phaseStart = watch.Elapsed;

        void EndPhase(string name)
        {
            Logs.ValidationPhase(volume.Logger, name, volume.Tree.Pages.PagesRead - phasePages, (watch.Elapsed - phaseStart).TotalMilliseconds);
            phasePages = volume.Tree.Pages.PagesRead;
            phaseStart = watch.Elapsed;
        }

        for (int i = 0; i < volume.Checkpoint.Roots.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = i;
            Guard(report, $"checkpoint root {index}", () => ReadEveryPage(volume.Tree, volume.Checkpoint.Roots[index], cancellationToken,
                Checkpoint.IsPhysicalRoot(index)));
        }

        EndPhase("checkpoint roots");
        IEnumerable<ulong> objects = [];
        Guard(report, "object table", () => objects = volume.Objects.GetIds(cancellationToken));
        Guard(report, "object table copies", () => CompareObjectTableCopies(volume, report, cancellationToken));

        Guard(report, "upcase table", () => UpcaseTable.Load(volume.Tree, volume.Objects, cancellationToken: cancellationToken));
        bool securityLoaded = Guard(report, "security table", () => volume.Security.EnsureLoaded(cancellationToken));
        EndPhase("upcase and security tables");

        // The directory walk comes first: listing a directory reads every page of its table, so the object pass below need not read
        // those trees a second time (on a volume with many directories that was about a third of all device reads).
        sink.Phase = ValidationPhase.Directories;
        sink.Notify(string.Empty);
        HashSet<ulong> listed = WalkDirectories(volume, report, verifyFileData, opener ?? volume, securityLoaded, sink, cancellationToken);
        EndPhase("directory walk");

        List<ulong> objectIds = [.. objects];
        sink.Phase = ValidationPhase.Objects;
        sink.ObjectsTotal = objectIds.Count;
        sink.Notify(string.Empty);

        foreach (ulong id in objectIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (listed.Contains(id) || Guard(report, $"object 0x{id:X}", () => ReadEveryPage(volume.Tree, volume.Objects.GetRoot(id), cancellationToken)))
            {
                report.Objects++;
            }

            sink.Notify(string.Empty);
        }

        EndPhase("object trees");

        report.MetadataPages = (ulong)(volume.Tree.Pages.PagesRead - initialPageReads);
        report.Elapsed = watch.Elapsed;
        Logs.ValidationFinished(volume.Logger, report.Objects, report.MetadataPages, report.Files, report.Issues.Count);
        return report;
    }

    /// <summary>Checkpoint root 5 (table id 4) holds a second object table (forefst FS_OTBL_003; the reader looks objects up in root 0 only).</summary>
    private const int ObjectTableCopyRoot = 5;

    /// <summary>
    ///     Compares the object table (root 0) with its copy (root 5): an object id with more than one row in either, or with a row in only one of
    ///     them, or with different rows, is reported. forefst found object ids with an earlier and a current row (GN_PREF_RA_004, the
    ///     win11refs2tmillionsofactions images); where such rows come from is not established. Nothing changes what the reader looks up.
    /// </summary>
    private static void CompareObjectTableCopies(ReFsVolume volume, ValidationReport report, CancellationToken cancellationToken)
    {
        if (volume.Checkpoint.Roots.Length <= ObjectTableCopyRoot)
        {
            return;
        }

        Dictionary<ulong, List<byte[]>> primary = ReadObjectRows(volume, Checkpoint.ObjectTableRoot, cancellationToken);
        Dictionary<ulong, List<byte[]>> copy = ReadObjectRows(volume, ObjectTableCopyRoot, cancellationToken);
        int reported = 0;

        void Add(string message)
        {
            if (reported++ < 50)
            {
                report.AddIssue("object table copies", message);
            }
        }

        foreach ((string name, Dictionary<ulong, List<byte[]>> rows) in new[] { ("root 0", primary), ("root 5", copy) })
            foreach ((ulong id, List<byte[]> values) in rows.Where(r => r.Value.Count > 1).OrderBy(r => r.Key))
        {
            Add($"object 0x{id:X} has {values.Count} rows in the object table at {name}");
        }

        foreach (ulong id in primary.Keys.Union(copy.Keys).OrderBy(static k => k))
        {
            bool inPrimary = primary.TryGetValue(id, out List<byte[]>? a);
            bool inCopy = copy.TryGetValue(id, out List<byte[]>? b);
            if (!inPrimary || !inCopy)
            {
                Add($"object 0x{id:X} is only in the object table at {(inPrimary ? "root 0" : "root 5")}");
            }
            else if (!a![^1].AsSpan().SequenceEqual(b![^1]))
            {
                Add($"object 0x{id:X} has different rows in the object tables at root 0 and root 5");
            }
        }

        if (reported > 50)
        {
            report.AddIssue("object table copies", $"{reported - 50} more difference(s) not listed");
        }
    }

    private static Dictionary<ulong, List<byte[]>> ReadObjectRows(ReFsVolume volume, int rootIndex, CancellationToken cancellationToken)
    {
        Dictionary<ulong, List<byte[]>> rows = [];
        foreach (EntryView entry in volume.Tree.Rows(volume.Checkpoint.Roots[rootIndex], cancellationToken))
        {
            if (entry.Key.Length != 16 || entry.Key.U64(0) != 0)
            {
                throw new ReFsFormatException($"Checkpoint root {rootIndex} has a row that is not an object table row.");
            }

            ulong id = entry.Key.U64(8);
            if (!rows.TryGetValue(id, out List<byte[]>? values))
            {
                rows[id] = values = [];
            }

            values.Add(entry.Value.ToArray());
        }

        return rows;
    }

    /// <summary>Walks a whole table so that every page of it is read (and its checksum verified).</summary>
    private static void ReadEveryPage(TreeReader tree, PageRef root, CancellationToken cancellationToken, bool physical = false)
    {
        foreach (EntryView _ in tree.Rows(root, cancellationToken, physical)) { }
    }

    private static string DescribeLog(ReFsVolume volume)
    {
        try
        {
            RedoLogScan? scan = volume.ReadRedoLog();
            return scan is null ? string.Empty : $" ({scan.Transactions.Count(t => t.Committed)} committed transaction(s) after the checkpoint)";
        }
        catch (Exception ex) when (ex is ReFsFormatException or IOException or NotSupportedException)
        {
            return $" (and cannot be read: {ex.Message})";
        }
    }

    private static HashSet<ulong> WalkDirectories(ReFsVolume volume, ValidationReport report, bool verifyFileData, IStreamOpener opener, bool securityLoaded, ProgressSink sink,
        CancellationToken cancellationToken)
    {
        HashSet<ulong> visitedDirectories = [DirectoryReader.RootObjectId];
        HashSet<ulong> listed = [];
        Stack<(ulong ObjectId, string Path, ReFsEntry? Entry)> pending = new Stack<(ulong ObjectId, string Path, ReFsEntry? Entry)>([(DirectoryReader.RootObjectId, string.Empty, null)]);
        byte[] buffer = new byte[BufferSize];

        Guard(report, "\\", () =>
        {
            ReFsEntry root = volume.Root;
            if (securityLoaded)
            {
                CheckSecurity(volume, report, root, "\\");
            }

            _ = volume.GetExtendedAttributes(root);
        });

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (ulong directoryObjectId, string directoryPath, ReFsEntry? self) = pending.Pop();

            // Each entry is checked as it is listed, while its page is the one just read. Listing the whole directory first made a
            // directory with more pages than the cache holds read every page twice. If the listing fails part-way, the entries
            // before the failure have been checked; the directory itself is reported and not counted.
            bool read = Guard(report, Describe(directoryPath), () =>
            {
                foreach (ReFsEntry entry in volume.Directories.Enumerate(directoryObjectId, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string path = directoryPath.Length == 0 ? entry.Name : directoryPath + "\\" + entry.Name;

                    if (entry.IsDirectory)
                    {
                        if (visitedDirectories.Add(entry.ObjectId))
                        {
                            pending.Push((entry.ObjectId, path, entry));
                        }
                        else
                        {
                            CheckOwnMetadata(volume, report, entry, path, securityLoaded);
                        }

                        continue;
                    }

                    CheckOwnMetadata(volume, report, entry, path, securityLoaded);
                    Guard(report, path, () => volume.CheckStreamSets(entry));

                    // Every name is checked; a hard-linked file's data is simply read once per name (file ids are only unique per directory).
                    report.Files++;
                    if (verifyFileData)
                    {
                        ReadStreams(opener, report, entry, path, buffer, sink, cancellationToken);
                    }
                }
            });

            // The directory's own security reference and extended attributes are in its own table: they are checked here, while
            // its pages are in the cache, not from the parent's listing (which cost a device read per directory).
            if (self is not null)
            {
                CheckOwnMetadata(volume, report, self, directoryPath, securityLoaded);
            }

            if (!read)
            {
                continue;
            }

            // A listing without replay read every page of the stored table. With replay the listing may come from the log instead.
            if (!volume.ReplayEnabled)
            {
                listed.Add(directoryObjectId);
            }

            report.Directories++;
            sink.Notify(directoryPath);
        }

        return listed;
    }

    private static void CheckOwnMetadata(ReFsVolume volume, ValidationReport report, ReFsEntry entry, string path, bool securityLoaded)
    {
        if (securityLoaded)
        {
            CheckSecurity(volume, report, entry, path);
        }

        Guard(report, path, () => _ = volume.GetExtendedAttributes(entry));
    }

    private static void CheckSecurity(ReFsVolume volume, ValidationReport report, ReFsEntry entry, string path)
    {
        if (entry.Replay == ReFsReplayState.Partial)
        {
            return;
        }

        Guard(report, path, () =>
        {
            if (!volume.TryGetSecurityDescriptor(entry, out _) && (entry.IsDirectory || entry.SecurityId != 0))
            {
                throw new ReFsFormatException("the security descriptor it refers to is not in the volume's security table");
            }
        });
    }

    private static void ReadStreams(IStreamOpener opener, ValidationReport report, ReFsEntry entry, string path, byte[] buffer, ProgressSink sink,
        CancellationToken cancellationToken)
    {
        if (entry.Replay == ReFsReplayState.Partial)
        {
            report.AddWarning($"{path}: its data was not replayed completely and was not read");
            return;
        }

        if (entry.Attributes.HasFlag(ReFsAttributes.Encrypted) && entry.Streams.Any(stream => stream.Length != 0))
        {
            report.AddWarning($"{path}: EFS-encrypted; its data was not read (ciphertext, not decrypted)");
            return;
        }

        foreach (ReFsStreamInfo info in entry.Streams)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string location = info.Name.Length == 0 ? path : $"{path}:{info.Name}";
            Guard(report, location, () =>
            {
                using Stream stream = opener.OpenRead(entry, info.Name.Length == 0 ? null : info.Name);
                ulong total = 0;
                ulong reported = 0;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    total += (ulong)read;
                    if (total - reported >= StreamProgressBytes)
                    {
                        reported = total;
                        sink.Notify(location, total);
                    }
                }

                if (total != info.Length)
                {
                    throw new ReFsFormatException($"read {total} bytes but the stream is {info.Length} bytes long");
                }

                report.DataBytes += total;
                report.Streams++;
            });

            sink.Notify(location);
        }
    }

    private static bool Guard(ValidationReport report, string location, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (ex is ReFsFormatException or ReFsCorruptionException or NotSupportedException or IOException or InvalidDataException)
        {
            report.AddIssue(location, $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string Describe(string path)
    {
        return path.Length == 0 ? "\\" : path;
    }

    /// <summary>Turns the report's counters into <see cref="ValidationProgress" /> snapshots; does nothing without a receiver.</summary>
    private sealed class ProgressSink(IProgress<ValidationProgress>? receiver, ValidationReport report)
    {
        public ValidationPhase Phase { get; set; } = ValidationPhase.Tables;

        public int ObjectsTotal { get; set; }

        /// <summary>Reports the counters; <paramref name="pendingBytes" /> are bytes of a stream still being read (not yet in the report).</summary>
        public void Notify(string path, ulong pendingBytes = 0)
        {
            receiver?.Report(new ValidationProgress(Phase, report.Directories, report.Files, report.Streams, report.DataBytes + pendingBytes, report.Issues.Count,
                report.Objects, ObjectsTotal, path));
        }
    }
}