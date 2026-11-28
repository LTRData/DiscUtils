using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.SystemTables;

namespace DiscUtils.ReFs.Replay;

/// <summary>
///     Where the redo log lives, from the Logfile Information Table (objects 0xA and 0x9, a failover pair; row key 1, 0x30 bytes):
///     {u64 f0 first cluster of the circular data region, u64 f1 = f0 + f2, u64 f2 data region length in clusters,
///     u64 f3 control page copy A (cluster), u64 f4 control page copy B (cluster), u64 f5 unknown}. All addresses are
///     real volume clusters (not container space), verified on 4 KiB and 64 KiB-cluster images.
/// </summary>
internal readonly record struct LogLocation(ulong DataStart, ulong DataClusters, ulong ControlA, ulong ControlB)
{
    /// <summary>
    ///     The copy Windows reads: the failover pair is {0xA, 0x9}; the first identifier becomes the active
    ///     table, and row key 1 is read from it alone.
    /// </summary>
    public const ulong ObjectId = 0xA;

    public const ulong MirrorObjectId = 0x9;

    private const uint InformationRowKey = 1;
    private const int RowLength = 0x30;
    private const ulong MinimumDataClusters = 80;

    /// <summary>
    ///     Reads object 0xA, and 0x9 when 0xA is missing, has no information row, or has a page that fails its checksum (Windows retries
    ///     a checksum failure on the partner copy). Lenient: Windows needs both records and the row.
    /// </summary>
    /// <exception cref="ReFsCorruptionException">Every existing copy has a damaged page.</exception>
    public static LogLocation? Load(TreeReader tree, ObjectTable objects, ILogger? logger = null)
    {
        LogLocation? location;

        try
        {
            location = Read(tree, objects, ObjectId);
        }
        catch (ReFsCorruptionException ex) when (objects.TryGetRoot(MirrorObjectId, out _))
        {
            Logs.MirroredTableFailover(logger ?? NullLogger.Instance, "logfile information", ObjectId, ex.Kind, ex.Lcn, MirrorObjectId);
            return Read(tree, objects, MirrorObjectId);
        }

        return location ?? Read(tree, objects, MirrorObjectId);
    }

    private static LogLocation? Read(TreeReader tree, ObjectTable objects, ulong objectId)
    {
        if (!objects.TryGetRoot(objectId, out var root))
        {
            return null;
        }

        foreach (var entry in tree.Rows(root))
        {
            var value = entry.Value;

            if (entry.Key.Length != 4 || entry.Key.U32(0) != InformationRowKey || value.Length < RowLength)
            {
                continue;
            }

            if (value.U64(0x10) < MinimumDataClusters)
            {
                throw new ReFsFormatException("The redo log region is implausibly small.");
            }

            return new LogLocation(value.U64(0), value.U64(0x10), value.U64(0x18), value.U64(0x20));
        }

        return null;
    }
}