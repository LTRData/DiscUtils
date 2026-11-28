using System.Collections.Immutable;
using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Mount.Enums;
using DiscUtils.ReFs.Replay;

namespace DiscUtils.ReFs.Mount;

/// <summary>
///     ReFS checkpoint record: the volume's global table roots as of the checkpoint. The layout is derived from observed Windows behaviour and real images.
///     It starts with the common header
///     (<see cref="MetadataPageHeader" />, signature "CHKP", own LCN checked); 0x50 and 0x52 u16 primary version (bounded by the boot version); 0x54 and
///     0x56 second version pair (role unresolved); 0x58 u32 offset and 0x5C u32 size of the self-checksum block (the descriptor used for validation; this
///     reader finds the block through it); 0x60 u64 virtual clock; 0x68 u64 restart clock (<see cref="RestartClock" />); 0x70 u64 redo log start LSN; 0x78
///     u32 features (<see cref="CheckpointFeatures" />; Windows sets 0x200 and 0x400); 0x88 u32 offset and 0x8C u32 size of the user payload; 0x90 u32
///     root count (Windows requires at least 5; this reader requires root 8); 0x94 u32 offset of the root array, which is indirect when feature
///     0x200 is set and the boot version is at least 3.11 (otherwise the array starts at 0x94); 0x98 u32 indirect array size; 0x9C u32 offset and 0xA0
///     u32 size of the rollback-protection blob (feature 0x400; not read). Roots are aligned page pointers (<see cref="PageRef" />) with individually
///     described digest lengths.
///     <para>
///         From format version 3.13, newer Windows builds can add an attestation HMAC area (feature 0x2000, <see cref="CheckpointFeatures.AttestationHmac" />):
///         0xA4 u32 offset and 0xA8 u32 size of the area, so the fixed header grows from 0xA8 to 0xB0 bytes and the root array starts at 0xB0. This reader
///         keeps the older 0xA8 bound, which new records also meet. The area is 8-aligned and follows the user payload and the rollback blob: +0x00 u32
///         total size (0x48 + digest length); +0x04 u32 flags; +0x08 and +0x18 two GUIDs; +0x28 u32 digest offset (0x48) and +0x2C u32 digest length, both
///         zero in an empty area; +0x30..+0x47 zero; +0x48 an HMAC-SHA256 digest. The self-checksum covers the area. This reader neither parses nor checks it;
///         the key is held outside the volume.
///     </para>
/// </summary>
internal sealed record Checkpoint : MetadataPageHeader
{
    private const uint Magic = 0x504B4843; // "CHKP"

    /// <summary>Index of the object table root (table id 2).</summary>
    public const int ObjectTableRoot = 0;

    /// <summary>Index of the container table root (mirrored at index 8).</summary>
    public const int ContainerTableRoot = 7;

    /// <summary>Index of the container table mirror; it uses the same physical address-space rule.</summary>
    internal const int ContainerTableMirrorRoot = 8;

    /// <summary>Allocator kind 2 forces physical addressing, overriding its E010 definition.</summary>
    internal const int PhysicalAllocatorRoot = 12;

    private const int MinimumRootCount = 9;

    /// <summary>A checkpoint from values, without a page behind it (tests); the common header is empty.</summary>
    internal Checkpoint(
        ulong clock,
        ushort majorVersion,
        ushort minorVersion,
        ImmutableArray<PageRef> roots,
        ReadOnlyMemory<byte> userPayload = default,
        ulong logStartLsn = 0,
        CheckpointFeatures features = CheckpointFeatures.None,
        ushort secondaryMajorVersion = 0,
        ushort secondaryMinorVersion = 0)
    {
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        SecondaryMajorVersion = secondaryMajorVersion;
        SecondaryMinorVersion = secondaryMinorVersion;
        Clock = clock;
        LogStartLsn = logStartLsn;
        Features = features;
        UserPayload = userPayload;
        Roots = roots;
    }

    /// <summary>Unpacks and validates the checkpoint read from cluster <paramref name="ownLcn" />.</summary>
    /// <exception cref="ReFsFormatException">The cluster is not a valid checkpoint.</exception>
    public Checkpoint(ReadOnlySpan<byte> cluster, ulong ownLcn) : base(cluster)
    {
        // In address order, the cheap checks first; what costs (the checksum over the whole block, then the copies) comes last.
        if (cluster.Length < 0xB0 || Signature != Magic) // 0x00
        {
            throw new ReFsFormatException("Missing checkpoint signature.");
        }

        if (Lcn0 != ownLcn) // 0x20
        {
            throw new ReFsFormatException("Checkpoint location does not match its address.");
        }

        MajorVersion = cluster.U16(0x50);
        MinorVersion = cluster.U16(0x52);
        SecondaryMajorVersion = cluster.U16(0x54);
        SecondaryMinorVersion = cluster.U16(0x56);
        var selfDescriptor = cluster.U32(0x58);
        var selfDescriptorLength = cluster.U32(0x5C);
        Clock = cluster.U64(0x60);
        RestartClock = cluster.U64(0x68);
        LogStartLsn = cluster.U64(0x70);
        Features = (CheckpointFeatures)cluster.U32(0x78);

        var payloadOffset = cluster.U32(0x88);
        var payloadLength = cluster.U32(0x8C);
        var rootCount = cluster.U32(0x90);
        var indirect = Features.HasFlag(CheckpointFeatures.ExtendedRecordLayout);
        var arrayOffset = indirect ? cluster.U32(0x94) : 0x94;

        if (rootCount < MinimumRootCount || arrayOffset + (4UL * rootCount) > (ulong)cluster.Length)
        {
            throw new ReFsFormatException("Unsupported checkpoint root table.");
        }

        if (indirect && (cluster.U32(0x98) < 4UL * rootCount || arrayOffset + (ulong)cluster.U32(0x98) > (ulong)cluster.Length))
        {
            throw new ReFsFormatException("Checkpoint root array length out of bounds.");
        }

        var minimumDataOffset = 0xA8 + (4UL * rootCount);

        if (payloadLength != 0 && (payloadOffset < minimumDataOffset || payloadOffset + (ulong)payloadLength > (ulong)cluster.Length))
        {
            throw new ReFsFormatException("Checkpoint user payload out of bounds.");
        }

        if (!SelfChecksum.Verify(cluster, selfDescriptor, selfDescriptorLength))
        {
            throw new ReFsFormatException("Checkpoint checksum mismatch.");
        }

        UserPayload = payloadLength == 0 ? ReadOnlyMemory<byte>.Empty : cluster.Slice((int)payloadOffset, (int)payloadLength).ToArray();

        var roots = ImmutableArray.CreateBuilder<PageRef>((int)rootCount);

        for (var i = 0; i < rootCount; i++)
        {
            var record = cluster.U32((int)arrayOffset + (4 * i));

            if (record < minimumDataOffset || (record & 7) != 0 || record + (ulong)PageRef.MinimumLength > (ulong)cluster.Length)
            {
                throw new ReFsFormatException($"Checkpoint root {i} out of bounds.");
            }

            var digestLength = cluster.U32((int)record + 0x24);
            var length = PageRef.MinimumLength + (ulong)digestLength;

            if (length > (ulong)cluster.Length - record)
            {
                throw new ReFsFormatException($"Checkpoint root {i} digest out of bounds.");
            }

            roots.Add(new PageRef(cluster.Slice((int)record, (int)length)));
        }

        Roots = roots.MoveToImmutable();
    }

    /// <summary>
    ///     Legacy, unverified shutdown heuristic: true when a user payload is present.
    ///     Windows passes that payload to USN journal setup; its presence does not establish a clean shutdown.
    ///     Retained for API compatibility, not as an authoritative dirty/replay decision.
    /// </summary>
    public bool WrittenAtCleanShutdown => !UserPayload.IsEmpty;

    /// <summary>u16 @0x50.</summary>
    public ushort MajorVersion { get; }

    /// <summary>u16 @0x52.</summary>
    public ushort MinorVersion { get; }

    /// <summary>u16 @0x54: second version pair; its role is not established.</summary>
    public ushort SecondaryMajorVersion { get; }

    /// <summary>u16 @0x56.</summary>
    public ushort SecondaryMinorVersion { get; }

    /// <summary>u64 @0x60: the virtual clock; the checkpoint copy with the higher value is the current one.</summary>
    public ulong Clock { get; }

    /// <summary>
    ///     u64 @0x68: the restart clock when the checkpoint was written. Redo records carry the same field at +0x20
    ///     (<see cref="RedoRecord.RestartClock" />); replay compares the two.
    /// </summary>
    public ulong RestartClock { get; init; }

    /// <summary>u64 @0x70: the log LSN that replay starts from.</summary>
    public ulong LogStartLsn { get; }

    /// <summary>u32 @0x78: the feature flags.</summary>
    public CheckpointFeatures Features { get; }

    /// <summary>The user payload; empty when the length is 0.</summary>
    public ReadOnlyMemory<byte> UserPayload { get; }

    /// <summary>The global table roots (u32 count @0x90, u32 offset @0x94 of the array of u32 offsets, one page pointer at each).</summary>
    public ImmutableArray<PageRef> Roots { get; }

    internal static bool IsPhysicalRoot(int index) => index is ContainerTableRoot or ContainerTableMirrorRoot or PhysicalAllocatorRoot;


    internal void ValidateVersion(byte bootMajor, byte bootMinor)
    {
        if (MajorVersion > byte.MaxValue || MinorVersion > byte.MaxValue
                                         || MajorVersion > bootMajor || (MajorVersion == bootMajor && MinorVersion > bootMinor))
        {
            throw new ReFsFormatException($"Checkpoint version {MajorVersion}.{MinorVersion} exceeds boot version {bootMajor}.{bootMinor}.");
        }

        if (Features.HasFlag(CheckpointFeatures.ExtendedRecordLayout) && (bootMajor < 3 || (bootMajor == 3 && bootMinor < 11)))
        {
            throw new ReFsFormatException("Indirect checkpoint root arrays require boot version 3.11 or later.");
        }
    }
}