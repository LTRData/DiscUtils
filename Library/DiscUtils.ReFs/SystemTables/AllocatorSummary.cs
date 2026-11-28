using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Mount;

namespace DiscUtils.ReFs.SystemTables;

/// <summary>
///     The allocator summary: the payload of an allocator table's root page, between the index root descriptor and the node header (0x78 of the page on
///     every image; Windows keeps 0x1A0 bytes). Windows loads it at mount, keeps it up to date as clusters are allocated and freed, writes it with each
///     checkpoint, and rebuilds it from the allocator's regions when it is out of sync. The volume's figures come from the summary of checkpoint root 1
///     (table 0x21), the allocator that covers the whole volume; Windows reports its free cluster count from the same summary.
///     <list type="table">
///         <listheader>
///             <term>Offset, size</term><description>Field</description>
///         </listheader>
///         <item>
///             <term>0x00, 8</term><description>Not identified (2 on root 1, 0 on roots 2 and 12 of the test images). Not read.</description>
///         </item>
///         <item>
///             <term>0x08, 8</term>
///             <description>Clusters the allocator covers. Windows refuses to mount when the volume allocator's is below the volume's cluster count.</description>
///         </item>
///         <item>
///             <term>0x10, 8</term>
///             <description>
///                 Clusters not allocated. On the test images this is Windows' "Free Clusters" plus "Total Reserved" (<c>fsutil fsinfo refsinfo</c>): the reserve
///                 is a set of runtime counters of the driver, not stored on the volume.
///             </description>
///         </item>
///         <item>
///             <term>0x18, 8</term><description>Global stream reserve, taken out of the free count; 0 on the test images except 1 on
///                 <c>w2025-refsdedup-shared-lz4</c>, where free minus this is exactly Windows' free plus reserved.</description>
///         </item>
///         <item>
///             <term>0x20, 8</term><description>Clusters allocated to metadata.</description>
///         </item>
///     </list>
///     The rest of the 0x1A0 bytes is not identified.
/// </summary>
internal readonly record struct AllocatorSummary
{
    /// <summary>Bytes this reader needs: the five leading u64 values.</summary>
    public const int MinimumLength = 0x28;

    /// <summary>Checkpoint root of the allocator that covers the whole volume.</summary>
    internal const int VolumeAllocatorRoot = 1;

    /// <summary>Table id of that allocator's pages.</summary>
    internal const ulong VolumeAllocatorTableId = 0x21;

    /// <summary>Unpacks a summary.</summary>
    /// <exception cref="ReFsFormatException">The bytes are too short, or the free or reserved count exceeds the clusters covered.</exception>
    public AllocatorSummary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < MinimumLength)
        {
            throw new ReFsFormatException($"Allocator summary too short ({bytes.Length} of {MinimumLength} bytes).");
        }

        Covered = bytes.U64(0x08);
        Free = bytes.U64(0x10);
        GlobalStreamReserve = bytes.U64(0x18);
        MetadataAllocated = bytes.U64(0x20);

        if (Free > Covered || GlobalStreamReserve > Covered || MetadataAllocated > Covered)
        {
            throw new ReFsFormatException(
                $"Allocator summary out of range (covered 0x{Covered:X}, free 0x{Free:X}, stream reserve 0x{GlobalStreamReserve:X}, metadata 0x{MetadataAllocated:X}).");
        }
    }

    /// <summary>Clusters the allocator covers (offset 0x08).</summary>
    public ulong Covered { get; }

    /// <summary>Clusters not allocated, the driver's runtime reserve included (offset 0x10).</summary>
    public ulong Free { get; }

    /// <summary>Global stream reserve, subtracted from the free count by Windows (offset 0x18).</summary>
    public ulong GlobalStreamReserve { get; }

    /// <summary>Clusters allocated to metadata (offset 0x20).</summary>
    public ulong MetadataAllocated { get; }

    /// <summary>Reads the summary of the volume allocator (checkpoint root 1) from its root page.</summary>
    /// <exception cref="ReFsFormatException">The root page belongs to another table, is not a table root, or its payload is not a valid summary.</exception>
    internal static AllocatorSummary Load(TreeReader tree, Checkpoint checkpoint)
    {
        if (checkpoint.Roots.Length <= VolumeAllocatorRoot)
        {
            throw new ReFsFormatException($"The checkpoint has no root {VolumeAllocatorRoot} (volume allocator).");
        }

        MetadataPage page = tree.Pages.Read(checkpoint.Roots[VolumeAllocatorRoot]);
        if (page.TableId != VolumeAllocatorTableId)
        {
            throw new ReFsFormatException($"Checkpoint root {VolumeAllocatorRoot} is table 0x{page.TableId:X}, not the volume allocator (0x{VolumeAllocatorTableId:X}).");
        }

        IndexRoot root = page.Root;
        if (root.Length == 0)
        {
            throw new ReFsFormatException($"Checkpoint root {VolumeAllocatorRoot} is not the root page of its table.");
        }

        // The payload lies between the descriptor and the node header; the page constructor checked the node offset against the page length.
        int start = MetadataPageHeader.HeaderLength + root.Length;
        if (start > page.NodeOffset)
        {
            throw new ReFsFormatException($"Checkpoint root {VolumeAllocatorRoot}: index root descriptor overlaps the node header.");
        }

        return new AllocatorSummary(page.Data.Span[start..page.NodeOffset]);
    }
}