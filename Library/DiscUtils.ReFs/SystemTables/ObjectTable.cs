using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Mount;

namespace DiscUtils.ReFs.SystemTables;

/// <summary>
///     The object table (checkpoint root 0, table id 2): object id -> root page of the object's table.
///     Row: key {u64 0, u64 objectId}; value: u8 type @0, u32 record offset @8 (0x18), record = {u64 LSN of the last
///     flush of the object's table, page pointer (LCN tuple + checksum blob)}. The LSN decides whether a redo
///     record is still needed.
/// </summary>
/// <remarks>
///     An object is looked up by key in the B+tree (a few pages), because the table has one row per directory and a volume with millions
///     of directories would otherwise be read completely (35 seconds and gigabytes of reads on a 4 TB volume) before the first file opens.
///     Keys compare by unsigned u64 at 0, then u64 at 8.
///     Counting or listing reads the whole table; a clean keyed miss does not. This API addresses the zero-prefix object-id namespace.
/// </remarks>
internal sealed class ObjectTable(TreeReader tree, PageRef root)
{
    private readonly ConcurrentDictionary<ulong, ObjectRecord> _found = new ConcurrentDictionary<ulong, ObjectRecord>();
    private readonly ConditionalWeakTable<MetadataPage, object> _validated = new ConditionalWeakTable<MetadataPage, object>();
    private object? _loadLock;
    private bool _loaded;

    private IReadOnlyDictionary<ulong, ObjectRecord>? _objects;

    /// <summary>True once the whole table was read by counting or listing.</summary>
    public bool IsFullyLoaded => Volatile.Read(ref _objects) is not null;

    public int Count => GetObjects(CancellationToken.None).Count;

    /// <summary>Every object id in the table (reads the whole table).</summary>
    public IEnumerable<ulong> Ids => GetIds(CancellationToken.None);

    public IEnumerable<ulong> GetIds(CancellationToken cancellationToken)
    {
        return GetObjects(cancellationToken).Keys;
    }

    public bool TryGetRoot(ulong objectId, out PageRef pointer, CancellationToken cancellationToken = default)
    {
        bool found = TryGetRecord(objectId, out ObjectRecord record, cancellationToken);
        pointer = record.Pointer;
        return found;
    }

    /// <summary>The LSN at which the object's table was last flushed (records below it are already in the table).</summary>
    public bool TryGetLsn(ulong objectId, out ulong lsn)
    {
        bool found = TryGetRecord(objectId, out ObjectRecord record);
        lsn = record.Lsn;
        return found;
    }

    public PageRef GetRoot(ulong objectId)
    {
        if (TryGetRoot(objectId, out PageRef pointer))
        {
            return pointer;
        }

        throw new DirectoryNotFoundException($"Object 0x{objectId:X} is not in the object table.");
    }

    private bool TryGetRecord(ulong objectId, out ObjectRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _objects) is { } objects)
        {
            return objects.TryGetValue(objectId, out record);
        }

        if (_found.TryGetValue(objectId, out record))
        {
            return true;
        }

        ObjectRecord? hit = null;
        tree.TryFind(root, key => CompareKey(key, objectId), (entry, _, _) => hit = Parse(entry), ValidatePage, cancellationToken);

        if (hit is { } found)
        {
            _found[objectId] = found;
            record = found;
            return true;
        }

        record = default;
        return false;
    }

    /// <summary>Sign of (row key compared with {u64 0, u64 objectId}), comparing the first word before the second.</summary>
    internal static int CompareKey(ReadOnlySpan<byte> key, ulong objectId)
    {
        if (key.Length != 16)
        {
            throw new ReFsFormatException("Malformed object table key.");
        }

        int byNamespace = key.U64(0).CompareTo(0UL);
        return byNamespace != 0 ? byNamespace : key.U64(8).CompareTo(objectId);
    }

    private void ValidatePage(MetadataPage page)
    {
        _validated.GetValue(page, static current =>
        {
            NodeView node = current.Node;
            int keys = node.IsLeaf ? node.Count : Math.Max(0, node.Count - 1);
            ReadOnlySpan<byte> previous = default;

            for (int i = 0; i < keys; i++)
            {
                ReadOnlySpan<byte> key = node[i].Key;
                ValidateKeyOrder(previous, key);
                previous = key;
            }

            return new object();
        });
    }

    private static void ValidateKeyOrder(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> key)
    {
        if (key.Length != 16)
        {
            throw new ReFsFormatException("Malformed object table key.");
        }

        if (!previous.IsEmpty && (previous.U64(0) > key.U64(0)
                                  || (previous.U64(0) == key.U64(0) && previous.U64(8) >= key.U64(8))))
        {
            throw new ReFsFormatException("Object table keys are duplicate or out of order.");
        }
    }

    private static ObjectRecord Parse(EntryView entry)
    {
        if (entry.Key.Length != 16 || entry.Value.Length < 12)
        {
            throw new ReFsFormatException("Malformed object table row.");
        }

        if (entry.Key.U64(0) != 0)
        {
            throw new NotSupportedException("Nonzero object table key prefixes are not supported by the object-id API.");
        }

        uint recordOffset = entry.Value.U32(8);
        if (recordOffset < 24 || recordOffset > entry.Value.Length
                              || sizeof(ulong) + (ulong)PageRef.MinimumLength > (ulong)entry.Value.Length - recordOffset)
        {
            throw new ReFsFormatException("Object table record offset out of bounds.");
        }

        ReadOnlySpan<byte> record = entry.Value[(int)recordOffset..];
        return new ObjectRecord(new PageRef(record[sizeof(ulong)..]), record.U64(0));
    }

    private IReadOnlyDictionary<ulong, ObjectRecord> GetObjects(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyDictionary<ulong, ObjectRecord>? result = LazyInitializer.EnsureInitialized(ref _objects, ref _loaded, ref _loadLock, () => Load(tree, root, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        return result!;
    }

    private static IReadOnlyDictionary<ulong, ObjectRecord> Load(TreeReader tree, PageRef root, CancellationToken cancellationToken)
    {
        Dictionary<ulong, ObjectRecord> objects = [];
        Span<byte> previous = stackalloc byte[16];
        bool hasPrevious = false;

        foreach (EntryView entry in tree.Rows(root, cancellationToken))
        {
            ValidateKeyOrder(hasPrevious ? previous : default, entry.Key);
            ObjectRecord record = Parse(entry);
            objects.Add(entry.Key.U64(8), record);
            entry.Key.CopyTo(previous);
            hasPrevious = true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyDictionary<ulong, ObjectRecord> result = objects;
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private readonly record struct ObjectRecord(PageRef Pointer, ulong Lsn);
}