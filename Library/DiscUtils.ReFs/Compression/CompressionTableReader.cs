using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Mount;

namespace DiscUtils.ReFs.Compression;

/// <summary>
///     Reads the compression table (table id 0xE: the checkpoint root whose page belongs to that table). Rows are keyed
///     {u64 container id, u32 row index, u32 row type}; type 7 rows are compression headers (index 0, 1, ...; other types are heat data),
///     type 3 index 0 is the cluster bitmap of the container.
/// </summary>
/// <remarks>
///     A container is looked up by key in the B+tree (a few pages). The table has rows for every compressed container: a 4 TB volume
///     had 17,811 of them and reading the whole table to open one file took minutes and gigabytes of reads. A container the tree
///     search does not find is looked for in the whole table, which is read once.
/// </remarks>
internal sealed class CompressionTableReader
{
    private const ulong CompressionTableId = 0xE;
    private const uint HeaderRowType = 7;
    private const uint BitmapRowType = 3;
    private const int MaximumHeadersPerContainer = 1 << 16;
    private readonly Lazy<CompressionTables> _full;
    private readonly Lazy<PageRef?> _root;

    private readonly TreeReader _tree;

    public CompressionTableReader(TreeReader tree, IReadOnlyList<PageRef> roots)
    {
        _tree = tree;
        _root = new Lazy<PageRef?>(() => FindRoot(tree, roots));
        _full = new Lazy<CompressionTables>(() => LoadAll(tree, _root.Value));
    }

    /// <summary>True once the whole table was read (a lookup the keyed search did not answer).</summary>
    internal bool IsFullyLoaded => _full.IsValueCreated;

    /// <summary>The whole table, read once (what a lookup falls back to; tests compare the keyed lookups with it).</summary>
    internal CompressionTables Full => _full.Value;

    public IReadOnlyList<CompressionHeader> GetHeaders(ulong containerId)
    {
        if (_root.Value is { } root)
        {
            List<CompressionHeader> headers = [];

            for (uint index = 0; index < MaximumHeadersPerContainer; index++)
            {
                var row = FindRow(root, containerId, index, HeaderRowType);

                if (row is null)
                {
                    break;
                }

                headers.Add(new CompressionHeader(row));
            }

            if (headers.Count > 0)
            {
                return headers;
            }
        }

        return _full.Value.Headers.TryGetValue(containerId, out var list)
            ? list
            : throw new ReFsFormatException($"Compressed container {containerId} has no compression headers.");
    }

    public byte[] GetBitmap(ulong containerId)
    {
        if (_root.Value is { } root && FindRow(root, containerId, 0, BitmapRowType) is { } row)
        {
            return ParseBitmap(containerId, row);
        }

        if (_full.Value.Bitmaps.TryGetValue(containerId, out var bitmap))
        {
            return bitmap;
        }

        throw new ReFsFormatException($"Compressed container {containerId} has no cluster bitmap.");
    }

    /// <summary>
    ///     Sign of (row key compared with the searched key). The rows are stored by row type (3, 5, 7), then container id, then row index
    ///     (seen on the compressed test images: all type 3 rows, then all type 5, then type 7 with a container's headers together).
    /// </summary>
    internal static int CompareKey(ReadOnlySpan<byte> key, ulong containerId, uint index, uint rowType)
    {
        if (key.Length != 16)
        {
            return key.Length < 16 ? -1 : 1;
        }

        var byType = key.U32(12).CompareTo(rowType);

        if (byType != 0)
        {
            return byType;
        }

        var byContainer = key.U64(0).CompareTo(containerId);
        return byContainer != 0 ? byContainer : key.U32(8).CompareTo(index);
    }

    private byte[]? FindRow(PageRef root, ulong containerId, uint index, uint rowType)
    {
        byte[]? value = null;
        _tree.TryFind(root, key => CompareKey(key, containerId, index, rowType), (entry, _, _) => value = entry.Value.ToArray());
        return value;
    }

    /// <summary>
    ///     The checkpoint root whose page belongs to the compression table. The roots of the container table, its mirror and the physical
    ///     allocator are physical addresses: they are skipped (the compression table is not one of them). Reading them through the container
    ///     map, as this did, read the wrong clusters once a volume had remapped the container they are in (a 4 TB volume in use: "checksum
    ///     mismatch for metadata page" when opening any compressed file, although nothing was damaged).
    /// </summary>
    private static PageRef? FindRoot(TreeReader tree, IReadOnlyList<PageRef> roots)
    {
        for (var i = 0; i < roots.Count; i++)
        {
            if (!Checkpoint.IsPhysicalRoot(i) && tree.Pages.Read(roots[i]).TableId == CompressionTableId)
            {
                return roots[i];
            }
        }

        return null;
    }

    /// <summary>
    ///     Header {.., u32 cluster count @0x20, u8 data offset @0x28}, then one bit per cluster at that offset (0x30 on every image). Windows takes the
    ///     data at the stored offset.
    /// </summary>
    internal static byte[] ParseBitmap(ulong containerId, ReadOnlySpan<byte> row)
    {
        var clusters = row.Length >= 0x29 ? row.U32(0x20) : 0;
        var dataOffset = row.Length >= 0x29 ? row[0x28] : 0;

        if (row.Length < 0x29 || dataOffset < 0x29 || clusters % 64 != 0 || (ulong)dataOffset + (clusters / 8UL) > (ulong)row.Length)
        {
            throw new ReFsFormatException($"Malformed cluster bitmap row for container {containerId}.");
        }

        return row.Slice(dataOffset, (int)(clusters / 8)).ToArray();
    }

    private static CompressionTables LoadAll(TreeReader tree, PageRef? root)
    {
        var tables = new CompressionTables();

        if (root is not { } compressionRoot)
        {
            return tables;
        }

        foreach (var entry in tree.Rows(compressionRoot))
        {
            if (entry.Key.Length != 16)
            {
                continue;
            }

            var containerId = entry.Key.U64(0);
            var rowType = entry.Key.U32(12);

            if (rowType == HeaderRowType)
            {
                if (!tables.Headers.TryGetValue(containerId, out var list))
                {
                    tables.Headers[containerId] = list = [];
                }

                list.Add(new CompressionHeader(entry.Value));
            }
            else if (rowType == BitmapRowType && entry.Key.U32(8) == 0)
            {
                tables.Bitmaps[containerId] = ParseBitmap(containerId, entry.Value);
            }
        }

        return tables;
    }
}