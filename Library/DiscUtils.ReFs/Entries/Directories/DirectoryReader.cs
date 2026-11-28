using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries.Enums;
using DiscUtils.ReFs.Entries.Files;
using DiscUtils.ReFs.Mount;
using DiscUtils.ReFs.Replay;
using DiscUtils.ReFs.SystemTables;

namespace DiscUtils.ReFs.Entries.Directories;

/// <summary>
///     Reads directory tables. Rows: key {u16 0x30, u16 kind, UTF-16LE name};
///     kind-1 value = embedded file table (<see cref="FileRecord" />), kind-2 value = a directory record or a hard-link record. Key {u32 0x10} holds the
///     directory's own metadata.
///     <para>
///         The directory record is built from the standard information (<see cref="StandardInformation" />); its on-disk form is 0x48 bytes (older) or 0x54 bytes. Layout:
///         0x00 FileId (u64), 0x08 a volume-level u64, 0x10 to 0x28 the four FILETIMEs, 0x30 and 0x38 the two sizes, 0x40 attributes (u32, with
///         <see cref="ReFsAttributes.DirectoryMarker" /> for a directory), 0x44 a u32 from the standard information (0x54), 0x48 a saturated u16 count
///         (the link count, inferred from standard information offset 0x50), 0x4A zero, 0x50 a u32 (only in the 0x54 form; unknown).
///     </para>
///     <para>
///         Sizes: the allocated size is at 0x30 and the size at 0x38 (a raw dump of a hard-linked file in forefst confirms this; an earlier version of this reader had
///         them swapped). Listed hard links use these per-name values; their streams resolve the backing record. Not settled: the object id of a
///         directory at 0x08 is read here and lists real directories correctly, while Windows appears to write a different value there for a file row.
///     </para>
/// </summary>
internal sealed class DirectoryReader(PageReader pages, TreeReader tree, ObjectTable objects, Lazy<UpcaseTable> upcase, RedoOverlay overlay, ILogger logger)
{
    public const ulong RootObjectId = 0x600;

    private const byte EntryType = 0x30;

    /// <summary>Directory records are 0x48 bytes (older volumes) or 0x54 bytes; all fields read here lie in the first 0x44 bytes.</summary>
    private const int DirectoryRecordMinimumLength = 0x48;

    private const int AttributesOffset = 0x40;

    /// <summary>
    ///     Directory record sizes: the allocated size at 0x30 and the size at 0x38 (they come from standard information offsets 56 and 48; a raw dump of a hard-linked file
    ///     agrees).
    /// </summary>
    private const int AllocatedOffset = 0x30;

    private const int SizeOffset = 0x38;

    private static readonly byte[] OwnRowKey = [0x10, 0, 0, 0];

    private readonly ConcurrentDictionary<ulong, bool> _caseSensitive = new ConcurrentDictionary<ulong, bool>();

    /// <summary>
    ///     Gets the stored root page of a directory table. False for a directory the replayed log created or deleted (no stored table to read).
    ///     Throws <see cref="DirectoryNotFoundException" /> for an object that exists nowhere.
    /// </summary>
    private bool TryGetStoredRoot(ulong directoryObjectId, out PageRef root, CancellationToken cancellationToken = default)
    {
        if (overlay.IsDeleted(directoryObjectId))
        {
            root = default;
            return false;
        }

        if (objects.TryGetRoot(directoryObjectId, out root, cancellationToken))
        {
            return true;
        }

        if (overlay.IsCreated(directoryObjectId))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        root = objects.GetRoot(directoryObjectId);
        return true;
    }

    /// <summary>Enumerates the entries of a directory in on-disk (key) order (replayed rows merged in by name when the volume replays the log).</summary>
    public IEnumerable<ReFsEntry> Enumerate(ulong directoryObjectId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Logs.DirectoryListed(logger, directoryObjectId, overlay.TryGetTable(directoryObjectId, out _) ? " (merged with the replayed redo log)" : string.Empty);
        var stored = TryGetStoredRoot(directoryObjectId, out var root, cancellationToken) ? EnumerateStored(root, cancellationToken) : [];
        return overlay.TryGetTable(directoryObjectId, out var table) ? MergeReplayed(stored, table!, cancellationToken) : stored;
    }

    /// <summary>The directory's own metadata (key 0x10 row).</summary>
    public ReFsEntry GetDirectoryEntry(string name, ulong directoryObjectId)
    {
        var s = StandardInformation.Parse(new EmbeddedTable(ReadOwnRowValue(directoryObjectId)).Payload, tree.StandardInformationMinimumLength);
        return new ReFsEntry(name, ReFsEntryKind.Directory, directoryObjectId, directoryObjectId, s.Attributes | ReFsAttributes.Directory,
            0, 0, s.Created, s.Modified, s.Changed, s.Accessed, []);
    }

    /// <summary>The value (an embedded table) of the directory's own metadata row, copied out of its page (or the replayed row).</summary>
    public byte[] ReadOwnRowValue(ulong directoryObjectId)
    {
        if (overlay.TryGetTable(directoryObjectId, out var replayed) && replayed!.TryGet(OwnRowKey, out var row) && row?.Value is not null)
        {
            return row.Value;
        }

        if (!TryGetStoredRoot(directoryObjectId, out var ownRoot))
        {
            throw new ReFsFormatException($"Directory 0x{directoryObjectId:X} has no metadata row.");
        }

        var page = pages.Read(ownRoot);

        while (!page.Node.IsLeaf)
        {
            var branch = page.Node;

            if (branch.Count == 0)
            {
                throw new ReFsFormatException($"Directory 0x{directoryObjectId:X} has an empty branch.");
            }

            page = pages.ReadChild(new PageRef(branch[0].Value), branch.Height);
        }

        var node = page.Node;

        for (var i = 0; i < node.Count; i++)
        {
            var entry = node[i];

            if (entry.Key.Length == 4 && entry.Key.U32(0) == 0x10)
            {
                return entry.Value.ToArray();
            }
        }

        throw new ReFsFormatException($"Directory 0x{directoryObjectId:X} has no metadata row.");
    }

    /// <summary>
    ///     True when the directory is flagged case-sensitive: lookups in it match the exact spelling only, as Windows does for a default (case-insensitive)
    ///     open. A missing or unreadable metadata row counts as not flagged. Cached per directory.
    /// </summary>
    public bool IsCaseSensitive(ulong directoryObjectId) => _caseSensitive.GetOrAdd(directoryObjectId, ReadCaseSensitive);

    private bool ReadCaseSensitive(ulong directoryObjectId)
    {
        try
        {
            return StandardInformation.Parse(new EmbeddedTable(ReadOwnRowValue(directoryObjectId)).Payload, tree.StandardInformationMinimumLength)
                                      .InternalFlags.HasFlag(StandardInformationInternalFlags.CaseSensitiveDirectory);
        }
        catch (ReFsFormatException)
        {
            return false;
        }
    }

    /// <summary>The security id stored in the directory's own metadata row.</summary>
    public ulong GetDirectorySecurityId(ulong directoryObjectId) =>
        StandardInformation.Parse(new EmbeddedTable(ReadOwnRowValue(directoryObjectId)).Payload, tree.StandardInformationMinimumLength).SecurityId;

    private static bool IsEntryKey(ReadOnlySpan<byte> key) => key.Length >= 4 && key[0] == EntryType && key[1] == 0 && (key.Length & 1) == 0;

    /// <summary>Merges replayed rows by folded name then raw spelling; only an exact replaced or deleted key hides a stored row.</summary>
    private IEnumerable<ReFsEntry> MergeReplayed(IEnumerable<ReFsEntry> stored, TableOverlay table, CancellationToken cancellationToken)
    {
        var fold = upcase.Value;
        List<(string Upper, ReFsEntry Entry)> added = [];

        foreach (var (key, row) in table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsEntryKey(key) && row.Value is not null && ParseReplayedRow(key, row) is { } entry)
            {
                added.Add((fold.Upper(entry.Name), entry));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        added.Sort(CompareNames);
        var next = 0;

        foreach (var entry in stored)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = entry.NamespaceKey
                      ?? throw new ReFsFormatException($"Directory entry '{entry.Name}' has no namespace key for replay.");

            var upper = fold.Upper(entry.Name);

            while (next < added.Count && CompareNames(added[next], (upper, entry)) < 0)
            {
                yield return added[next++].Entry;
            }

            if (!table.TryGet(key, out _))
            {
                yield return entry;
            }
        }

        while (next < added.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return added[next++].Entry;
        }

        static int CompareNames((string Upper, ReFsEntry Entry) left, (string Upper, ReFsEntry Entry) right)
        {
            var folded = string.CompareOrdinal(left.Upper, right.Upper);
            return folded != 0 ? folded : string.CompareOrdinal(left.Entry.Name, right.Entry.Name);
        }
    }

    private ReFsEntry? ParseReplayedRow(byte[] key, OverlayRow row)
    {
        var name = Encoding.Unicode.GetString(key.AsSpan(4));
        var location = EntryLocation.Replayed(key, row.Value!, row.Incomplete);
        var state = row.Incomplete is null ? ReFsReplayState.Replayed : ReFsReplayState.Partial;
        var entry = key.AsSpan().U16(2) switch
        {
            (ushort)ReFsEntryKind.File => ParseFile(name, row.Value!) with { Location = location },
            (ushort)ReFsEntryKind.Directory => ParseKindTwo(name, row.Value!, location),
            _ => null
        };

        if (entry is null)
        {
            return null;
        }

        entry = entry with { NamespaceKey = key };
        // A row whose embedded table was not fully replayed keeps its replayed size but must not advertise stale streams.
        return row.Incomplete is null || entry.IsDirectory
            ? entry with { Replay = state }
            : entry with { Replay = state, Streams = [new ReFsStreamInfo(string.Empty, entry.Length, false)] };
    }

    private IEnumerable<ReFsEntry> EnumerateStored(PageRef root, CancellationToken cancellationToken)
    {
        foreach (var page in tree.LeafPages(root, cancellationToken))
        foreach (var entry in ParseLeaf(page, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    /// <summary>
    ///     Finds a direct child, preferring exact spelling before a case-insensitive match; a case-sensitive directory
    ///     (<see cref="IsCaseSensitive" />) gets the exact match only. Stored rows use keyed searches; replayed directories
    ///     scan the merged namespace so exact-key tombstones preserve case variants.
    /// </summary>
    public bool TryFind(ulong directoryObjectId, string name, [NotNullWhen(true)] out ReFsEntry? child)
    {
        child = null;
        var hasStoredRoot = TryGetStoredRoot(directoryObjectId, out var storedRoot);

        if (overlay.TryGetTable(directoryObjectId, out _))
        {
            child = FindByScan(directoryObjectId, name);
            return child is not null;
        }

        if (!hasStoredRoot)
        {
            return false;
        }

        var table = upcase.Value;
        var upper = table.Upper(name);
        ReFsEntry? found = null;
        tree.TryFind(
            storedRoot,
            entryKey => DirectoryKeyOrder.Compare(entryKey, upper, table, name),
            (entry, page, slot) => found = ParseRow(entry, page, slot));

        if (found is null && !IsCaseSensitive(directoryObjectId))
        {
            tree.TryFind(storedRoot, entryKey => DirectoryKeyOrder.Compare(entryKey, upper, table), (entry, page, slot) => found = ParseRow(entry, page, slot));
        }

        child = found;
        return child is not null;
    }

    /// <summary>
    ///     Scans names without assuming key order, preferring an exact spelling over the first case-insensitive match
    ///     (exact only in a case-sensitive directory).
    /// </summary>
    /// <remarks>Unmatched record values are not parsed; full enumeration and validation still inspect them.</remarks>
    public ReFsEntry? FindByScan(ulong directoryObjectId, string name)
    {
        ReFsEntry? insensitive = null;
        var fold = upcase.Value;
        var exactOnly = IsCaseSensitive(directoryObjectId);

        if (overlay.TryGetTable(directoryObjectId, out _))
        {
            foreach (var candidate in Enumerate(directoryObjectId))
            {
                if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                {
                    return candidate;
                }

                if (!exactOnly && insensitive is null && fold.Equal(candidate.Name, name))
                {
                    insensitive = candidate;
                }
            }

            return insensitive;
        }

        if (!TryGetStoredRoot(directoryObjectId, out var root))
        {
            return null;
        }

        var rows = tree.Rows(root);

        while (rows.MoveNext())
        {
            var row = rows.Current;
            var key = row.Key;

            if (!IsEntryKey(key) || key.U16(2) is not ((ushort)ReFsEntryKind.File or (ushort)ReFsEntryKind.Directory))
            {
                continue;
            }

            var candidateName = Encoding.Unicode.GetString(key[4..]);
            var exact = string.Equals(candidateName, name, StringComparison.Ordinal);

            if (!exact && (exactOnly || insensitive is not null || !fold.Equal(candidateName, name)))
            {
                continue;
            }

            var page = rows.Page ?? throw new ReFsFormatException("A directory row has no backing page.");
            var candidate = ParseRow(row, page, rows.Slot);

            if (exact)
            {
                return candidate;
            }

            insensitive = candidate;
        }

        return insensitive;
    }

    private List<ReFsEntry> ParseLeaf(MetadataPage page, CancellationToken cancellationToken)
    {
        List<ReFsEntry> result = [];
        var node = page.Node;

        for (var i = 0; i < node.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ParseRow(node[i], page, i) is { } entry)
            {
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>Returns the file or directory entry for a leaf row, or null for other row types.</summary>
    private ReFsEntry? ParseRow(EntryView entry, MetadataPage page, int slot)
    {
        var key = entry.Key;

        if (!IsEntryKey(key))
        {
            return null; // own-metadata row, file-id back-links, unknown row types
        }

        var name = Encoding.Unicode.GetString(key[4..]);
        var parsed = key.U16(2) switch
        {
            (ushort)ReFsEntryKind.File => ParseFile(name, entry.Value) with { Location = new EntryLocation(page.Source, slot) },
            (ushort)ReFsEntryKind.Directory => ParseKindTwo(name, entry.Value, new EntryLocation(page.Source, slot)),
            _ => null
        };

        return parsed is null || overlay.IsEmpty ? parsed : parsed with { NamespaceKey = key.ToArray() };
    }

    /// <summary>
    ///     A directory's times as Windows shows them: the standard information of the directory's own metadata row (key 0x10, stored or replayed) wins
    ///     over the name row in the parent. Derived from images. Evidence: ReFS 3.1 can log new times only in the own row (lab copy
    ///     w2016-dirtynames, directory 'd'; the parent's row is not updated), and on w2025-churn1 the stored name rows of 19 of 400 directories hold older
    ///     change times than their stored own rows, which Windows' read-back shows. When the own row cannot be read the name row is used.
    /// </summary>
    /// <summary>
    ///     The directory's times and reparse point from its own metadata row, which is read once for both. A row that cannot be read leaves the entry as
    ///     listed, except that a flagged directory still fails on a corrupt or missing row (as when the reparse point read the row a second time).
    /// </summary>
    private ReFsEntry WithOwnRow(ReFsEntry directory)
    {
        var reparse = directory.Attributes.HasFlag(ReFsAttributes.ReparsePoint);
        byte[] value;

        try
        {
            value = ReadOwnRowValue(directory.ObjectId);
        }
        catch (Exception ex) when (ex is ReFsFormatException || (!reparse && ex is ReFsCorruptionException or DirectoryNotFoundException))
        {
            return directory;
        }

        // A payload too short for standard information also fails the record parse, so the entry keeps neither times nor reparse point.
        var payload = new EmbeddedTable(value).Payload;

        if (payload.Length < Math.Max(tree.StandardInformationMinimumLength, StandardInformation.MinimumLength))
        {
            return directory;
        }

        var own = StandardInformation.Parse(payload, tree.StandardInformationMinimumLength);
        return WithReparsePoint(directory with { Created = own.Created, Modified = own.Modified, Changed = own.Changed, Accessed = own.Accessed }, value);
    }

    /// <summary>
    ///     Kind-2 rows are directories (first qword zero) or link records of hard-linked files.
    ///     Links retain per-name metadata and resolve the backing record for streams and data access.
    /// </summary>
    private ReFsEntry ParseKindTwo(string name, ReadOnlySpan<byte> value, EntryLocation location)
    {
        if (value.Length < DirectoryRecordMinimumLength)
        {
            throw new ReFsFormatException($"Directory record for '{name}' is too short.");
        }

        // Replayed directory rows need not carry the stored-page DirectoryMarker attribute.
        if (value.U64(0) == 0)
        {
            return WithOwnRow(ParseDirectory(name, value)) with { Location = location };
        }

        var fileId = value.U64(0);
        var holderDirectory = value.U64(8);

        if (TryFindLinkedRecord(name, fileId, holderDirectory, out var linked))
        {
            return linked with
            {
                HolderDirectory = holderDirectory,
                Attributes = ((ReFsAttributes)value.U32(AttributesOffset)).AsListed(),
                Length = value.U64(SizeOffset),
                AllocatedLength = value.U64(AllocatedOffset),
                Created = value.FileTime(0x10),
                Modified = value.FileTime(0x18),
                Changed = value.FileTime(0x20),
                Accessed = value.FileTime(0x28)
            };
        }

        return new ReFsEntry(
            name,
            ReFsEntryKind.File,
            fileId,
            0,
            ((ReFsAttributes)value.U32(AttributesOffset)).AsListed(),
            value.U64(SizeOffset),
            value.U64(AllocatedOffset),
            value.FileTime(0x10),
            value.FileTime(0x18),
            value.FileTime(0x20),
            value.FileTime(0x28),
            [new ReFsStreamInfo(string.Empty, value.U64(SizeOffset), false)]);
    }

    /// <summary>
    ///     Finds the file record of a hard-linked file: row key {u32 0x80000040, u32 0, u64 fileId, u64 holder directory}
    ///     in the holder directory's table. Searches by key and falls back to a scan.
    /// </summary>
    private bool TryFindLinkedRecord(string name, ulong fileId, ulong holderDirectory, [NotNullWhen(true)] out ReFsEntry? linked)
    {
        linked = null;

        // A link record the log wrote (or moved) lives in the overlay of its holder directory.
        var linkKey = new byte[24];
        BitConverter.TryWriteBytes(linkKey.AsSpan(0), (uint)RowType.LinkRecord);
        BitConverter.TryWriteBytes(linkKey.AsSpan(8), fileId);
        BitConverter.TryWriteBytes(linkKey.AsSpan(16), holderDirectory);

        if (overlay.TryGetTable(holderDirectory, out var replayed) && replayed!.TryGet(linkKey, out var linkRow))
        {
            // The overlay knows the row: either its replayed value, or that the log deleted it.
            if (linkRow?.Value is null)
            {
                return false;
            }

            linked = ParseFile(name, linkRow.Value) with { Location = EntryLocation.Replayed(linkKey, linkRow.Value, linkRow.Incomplete) };
            return true;
        }

        if (!TryGetStoredRoot(holderDirectory, out var root))
        {
            return false;
        }

        ReFsEntry? found = null;
        tree.TryFind(
            root,
            rowKey => DirectoryKeyOrder.CompareLinkRecord(rowKey, fileId, holderDirectory),
            (entry, page, slot) => found = ParseFile(name, entry.Value) with { Location = new EntryLocation(page.Source, slot) });

        if (found is null)
        {
            var rows = tree.Rows(root);

            while (rows.MoveNext())
            {
                var entry = rows.Current;

                if (DirectoryKeyOrder.CompareLinkRecord(entry.Key, fileId, holderDirectory) != 0)
                {
                    continue;
                }

                var page = rows.Page
                           ?? throw new ReFsFormatException($"Linked record for '{name}' has no metadata page.");

                found = ParseFile(name, entry.Value) with { Location = new EntryLocation(page.Source, rows.Slot) };
                break;
            }
        }

        linked = found;
        return found is not null;
    }

    /// <summary>
    ///     A directory's reparse point (junction, directory symbolic link) is a row of its own metadata record, in its own
    ///     table; the name row in the parent only carries the attribute. The record is parsed only for flagged directories.
    ///     A directory whose record cannot be parsed is listed without reparse data.
    /// </summary>
    private ReFsEntry WithReparsePoint(ReFsEntry directory, byte[] ownRow)
    {
        if (!directory.Attributes.HasFlag(ReFsAttributes.ReparsePoint))
        {
            return directory;
        }

        try
        {
            return directory with { Reparse = new FileRecord(tree, ownRow).GetReparsePoint() };
        }
        catch (ReFsFormatException)
        {
            return directory;
        }
    }

    /// <summary>
    ///     Every leaf row of a directory table (including rows normal listing skips), for diagnostics. When the volume replays the log, a
    ///     replayed row replaces the stored row of the same key in place or hides it (deleted), and rows only the log has follow the stored
    ///     ones in byte order of their keys.
    /// </summary>
    public List<(byte[] Key, byte[] Value)> ReadAllRows(ulong directoryObjectId)
    {
        overlay.TryGetTable(directoryObjectId, out var replayed);
        List<(byte[] Key, byte[] Value)> rows = [];

        if (TryGetStoredRoot(directoryObjectId, out var root))
        {
            foreach (var entry in tree.Rows(root))
            {
                var key = entry.Key.ToArray();

                if (replayed is null || !replayed.TryGet(key, out var row))
                {
                    rows.Add((key, entry.Value.ToArray()));
                }
                else if (row!.Value is not null)
                {
                    rows.Add((key, row.Value));
                }
            }
        }

        if (replayed is null)
        {
            return rows;
        }

        var listed = new HashSet<byte[]>(rows.Select(r => r.Key), TableOverlay.ByteArrayComparer.Instance);
        rows.AddRange(replayed.Rows
                              .Where(r => r.Value.Value is not null && !listed.Contains(r.Key))
                              .OrderBy(r => r.Key, TableOverlay.ByteArrayComparer.Instance)
                              .Select(r => (r.Key, r.Value.Value!)));

        return rows;
    }

    internal static ReFsEntry ParseDirectory(string name, ReadOnlySpan<byte> value)
    {
        if (value.Length < DirectoryRecordMinimumLength)
        {
            throw new ReFsFormatException($"Directory record for '{name}' is too short.");
        }

        var objectId = value.U64(8);
        return new ReFsEntry(
            name,
            ReFsEntryKind.Directory,
            objectId,
            objectId,
            ((ReFsAttributes)value.U32(AttributesOffset) | ReFsAttributes.Directory).AsListed(),
            0,
            0,
            value.FileTime(0x10),
            value.FileTime(0x18),
            value.FileTime(0x20),
            value.FileTime(0x28),
            []);
    }

    private ReFsEntry ParseFile(string name, ReadOnlySpan<byte> value)
    {
        var record = new FileRecord(tree, value);
        var info = record.Info;
        var (streams, reparse) = record.GetStreamsAndReparsePoint();

        return new ReFsEntry(
            name,
            ReFsEntryKind.File,
            info.ChildOrdinal,
            0,
            info.Attributes,
            info.Length,
            info.AllocatedLength,
            info.Created,
            info.Modified,
            info.Changed,
            info.Accessed,
            streams)
        {
            Reparse = reparse,
            SecurityId = info.SecurityId,
            KeptFileId = info.ExternalFileId3 != 0 ? (info.ExternalFileId3, info.ExternalFileId2) : default
        };
    }
}