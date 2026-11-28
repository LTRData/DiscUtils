using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Compression;
using DiscUtils.ReFs.Compression.Enums;
using DiscUtils.ReFs.Mount;
using DiscUtils.ReFs.SystemTables.Enums;

namespace DiscUtils.ReFs.SystemTables;

/// <summary>
///     Translates container-space cluster numbers (as stored in page pointers) to real volume clusters.
///     containerLcn = (containerId &lt;&lt; (s + 1)) | offset, where a container is 2^s clusters; id 0 is identity. Windows has no such case: it looks every
///     id up in the container table, but no container table has an id-0 row (the test image's lowest id is 2), so on a volume Windows mounts no virtual
///     address has id 0 and the rule only decides what a damaged volume reads.
///     Compressed containers cannot be translated: their data is read through <see cref="CompressedContainer" />.
/// </summary>
internal sealed class ContainerMap
{
    private const int DefaultContainerBits = 14; // 16384 clusters (64 MiB at 4 KiB clusters)

    private readonly bool _identity;
    private readonly ConcurrentDictionary<ulong, CompressedContainer> _open = new();
    private readonly IReadOnlyDictionary<ulong, ContainerRecord> _records;

    private ContainerMap(IReadOnlyDictionary<ulong, ContainerRecord> records, int containerBits, ILogger logger, bool identity = false)
    {
        _records = records;
        ContainerBits = containerBits;
        _identity = identity;
        Logger = logger;
    }

    /// <summary>
    ///     Bootstrap map for reading the container table itself: every cluster number is a real cluster. The table cannot be addressed
    ///     through itself, so the pointers to its pages are real addresses even when they lie above the first container (a volume whose
    ///     container table sits beyond cluster 2^15 would otherwise be read as "unknown container").
    /// </summary>
    public static ContainerMap IdentityOnly { get; } = new ContainerMap(new Dictionary<ulong, ContainerRecord>(), DefaultContainerBits, NullLogger.Instance, true);

    /// <summary>Logger of the map (category <c>ReFs.Containers</c>).</summary>
    internal ILogger Logger { get; }

    /// <summary>log2 of the number of clusters per container.</summary>
    public int ContainerBits { get; }

    public int Count => _records.Count;

    /// <summary>Set once the volume is mounted; required to read compressed containers.</summary>
    internal CompressionContext? Compression { get; set; }

    /// <summary>Compressed containers currently in the table.</summary>
    internal int CompressedCount => _records.Values.Count(r => r.IsCompressed);

    /// <summary>Ids of the compressed or packed containers.</summary>
    internal IEnumerable<ulong> CompressedIds => _records.Where(r => r.Value.IsCompressed).Select(r => r.Key);

    /// <summary>Containers compressed with <paramref name="format" /> (and <paramref name="chunkSize" />, or any when 0).</summary>
    internal int CountCompressed(CompressionFormat format, uint chunkSize)
    {
        return _records.Values.Count(r => r.Flags.HasFlag(ContainerFlags.Compressed) && r.Format == format && (chunkSize == 0 || r.ChunkSize == chunkSize));
    }

    public ulong Translate(ulong containerLcn)
    {
        ulong id = containerLcn >> (ContainerBits + 1);
        if (id == 0 || _identity)
        {
            return containerLcn;
        }

        if (!_records.TryGetValue(id, out ContainerRecord record))
        {
            throw new ReFsFormatException(
                $"Cluster 0x{containerLcn:X} belongs to unknown container {id} (containers are 2^{ContainerBits} clusters; the container table has {_records.Count} container(s)"
                + (_records.Count == 0 ? ")." : $", ids {_records.Keys.Min()}..{_records.Keys.Max()}, missing in that range: {MissingIds()})."));
        }

        if (record.IsCompressed)
        {
            throw new NotSupportedException($"Container {id} is compressed; its clusters cannot be addressed directly.");
        }

        return record.Locate(containerLcn & ((1UL << ContainerBits) - 1));
    }

    /// <summary>Ids absent between the lowest and highest id of the table (diagnostics for the unknown-container error).</summary>
    private string MissingIds()
    {
        List<ulong> missing = [];
        ulong min = _records.Keys.Min();
        ulong max = _records.Keys.Max();
        for (ulong id = min; id <= max && missing.Count < 20; id++)
        {
            if (!_records.ContainsKey(id))
            {
                missing.Add(id);
            }
        }

        return missing.Count == 0 ? "none" : string.Join(", ", missing) + (missing.Count == 20 ? ", ..." : string.Empty);
    }

    /// <summary>Number of clusters from <paramref name="containerLcn" /> to the end of its container.</summary>
    public ulong ContiguousClusters(ulong containerLcn)
    {
        return (1UL << ContainerBits) - OffsetInContainer(containerLcn);
    }

    /// <summary>Cluster offset of <paramref name="containerLcn" /> within its container.</summary>
    internal ulong OffsetInContainer(ulong containerLcn)
    {
        return containerLcn & ((1UL << ContainerBits) - 1);
    }

    /// <summary>Gets the reader of the container holding <paramref name="containerLcn" /> when that container is compressed.</summary>
    internal bool TryGetCompressed(ulong containerLcn, [NotNullWhen(true)] out CompressedContainer? container)
    {
        container = null;
        var id = containerLcn >> (ContainerBits + 1);
        if (id == 0 || !_records.TryGetValue(id, out var record) || !record.IsCompressed)
        {
            return false;
        }

        var context = Compression
                      ?? throw new NotSupportedException($"Container {id} is compressed, but no compression context is attached.");

        container = _open.GetOrAdd(id, _ => Open(id, record, context));
        return true;
    }

    /// <summary>
    ///     Builds the map from the container table. Row: key u64 container id; value: u32 flags @0x14, u32 size @0x18,
    ///     u32 used clusters @0x20, u32 run count @0x4C, runs {u64 start, u64 count} at the end of the value.
    ///     A compressed container (<see cref="ContainerFlags.Compressed" />) also has u16 format @0x30 and u32 chunk size @0x34; its single run is the packed
    ///     compressed stream. Other containers must be single-run and fully mapped (anything else fails closed).
    /// </summary>
    internal static ContainerMap Build(TreeReader tree, PageRef root, ILogger log)
    {
        Dictionary<ulong, ContainerRecord> records = [];
        int? bits = null;

        foreach (EntryView entry in tree.Rows(root, CancellationToken.None, true))
        {
            ContainerRecord record = new ContainerRecord(entry.Key, entry.Value, tree.Pages.ClusterSize);
            ulong id = record.Id;
            int rowBits = Log2(record.Size);
            if (bits is { } known && known != rowBits)
            {
                throw new NotSupportedException("Containers of different sizes are not supported.");
            }

            bits = rowBits;
            if (!records.TryAdd(id, record))
            {
                throw new ReFsFormatException($"Duplicate container identifier {id}.");
            }

            Logs.ContainerRow(log, id, record.Start, record.RunClusters, record.Flags, record.Format);
        }

        ContainerMap map = new ContainerMap(records, bits ?? DefaultContainerBits, log);
        Logs.ContainerTable(log, map.Count, map.ContainerBits, map.CompressedCount);
        return map;
    }

    private static int Log2(uint value)
    {
        var result = 0;
        while ((value >>= 1) != 0)
        {
            result++;
        }

        return result;
    }

    private CompressedContainer Open(ulong id, ContainerRecord record, CompressionContext context)
    {
        var packed = record.Format == CompressionFormat.None;
        if (!packed && !context.Decompressor.Supports(record.Format))
        {
            throw new NotSupportedException($"Container {id} uses compression format {record.Format}, which is not supported on this target framework.");
        }

        return new CompressedContainer(
            context.Volume,
            record.StreamRuns ?? [(record.Start, record.RunClusters)],
            this,
            record.Format,
            record.ChunkSize,
            packed ? [] : context.GetHeaders(id),
            context.Decompressor,
            context.ClusterSize,
            context.GetBitmap(id),
            context.VerifyChecksums);
    }
}