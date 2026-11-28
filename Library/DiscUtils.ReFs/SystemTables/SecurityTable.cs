using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Mount;
using DiscUtils.ReFs.Replay;

namespace DiscUtils.ReFs.SystemTables;

/// <summary>
/// The volume's shared security descriptors (object 0x530). A file or directory record names its descriptor by a 64-bit security id (standard
/// information payload +0x28: {u32 hash, u32 1}). Row key: {u64 value length, u64 id with the two dwords swapped}, so rows are ordered by length and
/// a lookup by id means loading the table once. Row value: {u32 hash, u32 1, u32 total length, self-relative security descriptor}; the descriptor is
/// returned as the raw blob. The descriptor is never parsed here.
/// </summary>
internal sealed class SecurityTable(TreeReader tree, ObjectTable objects, RedoOverlay overlay)
{
    public const ulong ObjectId = 0x530;

    private const int HeaderLength = 12;

    /// <summary>Size of a self-relative security descriptor header (revision, control, four offsets).</summary>
    private const int MinimumDescriptorLength = 20;

    private IReadOnlyDictionary<ulong, byte[]>? _descriptors;
    private object? _loadLock;
    private bool _loaded;

    public int Count => GetDescriptors(CancellationToken.None).Count;

    public void EnsureLoaded(CancellationToken cancellationToken)
    {
        _ = GetDescriptors(cancellationToken);
    }

    /// <summary>Returns the raw descriptor bytes for a security id; false when the id is 0 or unknown.</summary>
    public bool TryGet(ulong securityId, out ReadOnlyMemory<byte> descriptor)
    {
        if (securityId != 0 && GetDescriptors(CancellationToken.None).TryGetValue(securityId, out byte[]? bytes))
        {
            descriptor = bytes;
            return true;
        }

        descriptor = default;
        return false;
    }

    private IReadOnlyDictionary<ulong, byte[]> GetDescriptors(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyDictionary<ulong, byte[]>? result = LazyInitializer.EnsureInitialized(ref _descriptors, ref _loaded, ref _loadLock, () => Load(tree, objects, overlay, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        return result!;
    }

    private static IReadOnlyDictionary<ulong, byte[]> Load(TreeReader tree, ObjectTable objects, RedoOverlay overlay, CancellationToken cancellationToken)
    {
        Dictionary<ulong, byte[]> descriptors = [];
        if (!objects.TryGetRoot(ObjectId, out PageRef root, cancellationToken))
        {
            return descriptors;
        }

        foreach (EntryView entry in tree.Rows(root, cancellationToken))
        {
            Add(descriptors, entry.Value);
        }

        // Descriptors added by replayed log records (new security ids); rows are never deleted or changed in place.
        if (overlay.TryGetTable(ObjectId, out TableOverlay? replayed))
        {
            foreach (OverlayRow row in replayed!.Rows.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.Value is not null)
                {
                    Add(descriptors, row.Value);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyDictionary<ulong, byte[]> result = descriptors;
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static void Add(Dictionary<ulong, byte[]> descriptors, ReadOnlySpan<byte> value)
    {
        if (value.Length < HeaderLength)
        {
            return; // other row types
        }

        uint total = value.U32(8);
        if (total < HeaderLength + MinimumDescriptorLength || total > value.Length)
        {
            throw new ReFsFormatException($"Malformed security row (length field {total}, value {value.Length} bytes).");
        }

        descriptors[value.U64(0)] = value[HeaderLength..(int)total].ToArray();
    }
}