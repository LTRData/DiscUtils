using System.Collections.Immutable;
using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.Mount;

/// <summary>
///     ReFS superblock: identifies the volume and points at the two checkpoint copies. There are three copies, at cluster 30, total - 2 and total - 3;
///     the mount reads all of them and uses the valid one with the highest generation (see <c>ReFsVolume.ReadSuperBlock</c>). The self-checksum record has a checksum
///     kind byte at +0x23 (8) and a digest length u32 at +0x24, at most 0x20. Layout: the common header (<see cref="MetadataPageHeader" />, signature
///     "SUPB", own LCN in the first location slot and the other three zero); 0x50 16 bytes the volume GUID; 0x68 u64 generation (the selection key; what
///     counts it up is unknown); 0x70 u32 checkpoint array offset and 0x74 u32 count; 0x78 u32 offset and 0x7C u32 length of the self-checksum record.
///     <para>
///         The checks below mirror the validation done by Windows: at least 0xC0 bytes, the self-checksum, the 32-byte location tuple equal to
///         {the cluster read, 0, 0, 0}, count 2, array offset at least 0xC0 and inside the block, each checkpoint cluster above 30 and at most total - 3.
///         Windows also requires the byte at 4 to be non-zero (not checked here); the "SUPB" signature is checked here.
///     </para>
/// </summary>
internal sealed record SuperBlock : MetadataPageHeader
{
    /// <summary>Cluster number of the primary superblock.</summary>
    public const ulong PrimaryLcn = 30;

    private const uint Magic = 0x42505553; // "SUPB"
    private const int MinimumSize = 0xC0;

    /// <summary>Unpacks and validates the superblock read from cluster <paramref name="ownLcn" />.</summary>
    /// <exception cref="ReFsFormatException">The cluster is not a valid superblock.</exception>
    public SuperBlock(ReadOnlySpan<byte> cluster, ulong ownLcn, ulong totalClusters) : base(cluster)
    {
        // In address order, the cheap checks first; what costs (the checksum over the whole block, then the array) comes last.
        if (cluster.Length < MinimumSize)
        {
            throw new ReFsFormatException("Superblock too short.");
        }

        if (Signature != Magic) // 0x00
        {
            throw new ReFsFormatException("Missing superblock signature.");
        }

        if (Lcn0 != ownLcn || Lcn1 != 0 || Lcn2 != 0 || Lcn3 != 0) // 0x20
        {
            throw new ReFsFormatException("Superblock location tuple does not match its address.");
        }

        VolumeId = new Guid(cluster.Slice(0x50, 16).ToArray());
        Generation = cluster.U64(0x68);

        var arrayOffset = cluster.U32(0x70);
        var count = cluster.U32(0x74);

        if (count != 2 || arrayOffset < MinimumSize || arrayOffset + (8UL * count) > (ulong)cluster.Length)
        {
            throw new ReFsFormatException("Unsupported superblock checkpoint array.");
        }

        var selfDescriptor = cluster.U32(0x78);
        var selfDescriptorLength = cluster.U32(0x7C);

        if (!SelfChecksum.Verify(cluster, selfDescriptor, selfDescriptorLength))
        {
            throw new ReFsFormatException("Superblock checksum mismatch.");
        }

        var lcns = ImmutableArray.CreateBuilder<ulong>((int)count);

        for (var i = 0; i < count; i++)
        {
            var lcn = cluster.U64((int)arrayOffset + (8 * i));

            if (lcn <= PrimaryLcn || lcn > totalClusters - 3)
            {
                throw new ReFsFormatException($"Checkpoint cluster 0x{lcn:X} out of range.");
            }

            lcns.Add(lcn);
        }

        CheckpointLcns = lcns.MoveToImmutable();
    }

    /// <summary>16 bytes @0x50.</summary>
    public Guid VolumeId { get; }

    /// <summary>u64 @0x68: the copy with the highest value is the current one.</summary>
    public ulong Generation { get; }

    /// <summary>The checkpoint clusters (u32 array offset @0x70, u32 count @0x74).</summary>
    public ImmutableArray<ulong> CheckpointLcns { get; }
}