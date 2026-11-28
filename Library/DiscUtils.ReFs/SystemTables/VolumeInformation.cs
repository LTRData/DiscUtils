using System.Text;
using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Mount;
using DiscUtils.ReFs.SystemTables.Enums;

namespace DiscUtils.ReFs.SystemTables;

/// <summary>
///     The volume information table: the volume's
///     label, versions, flags and creation and mount times. Rows have an 8-byte key, a u16 row type then six zero bytes:
///     <list type="table">
///         <listheader>
///             <term>Key type</term><description>Value</description>
///         </listheader>
///         <item>
///             <term>0x510</term>
///             <description>The label: raw UTF-16LE, no terminator and no length prefix.</description>
///         </item>
///         <item>
///             <term>0x520</term><description>The 448-byte volume information record, described below. Required.</description>
///         </item>
///         <item>
///             <term>0x540</term>
///             <description>
///                 16 bytes, the verify block pointer: u64 sector number, u64 sector count (1 to 0xFFFFFFFF). At mount Windows
///                 appears to write a copy of the 0x520 blob there. Test images: sector 35, count 1. (forefst calls these a schema count and flags.)
///             </description>
///         </item>
///     </list>
///     <para>
/// The two objects 0x500 and 0x501 form a durable failover pair; 0x501 is byte-identical to 0x500 on the test images.
///     </para>
///     <para>
/// Layout of the 0x520 row, inferred from observed behaviour of Windows and the test images:
/// 0x80 and 0x81 two bytes, the on-disk format version major and minor; 0x82 and 0x83 the u16 constant 0x0E03
/// (bytes 03 0E) written at mount; 0x84 u32 <see cref="VolumeFlags" />; 0x90 and 0x98 u64 times, never changed after the row is created;
/// 0xA0 u64 written at mount with the current time
/// (bumped by 1 when it equals the old value); 0xB0 u16 written together with 0x80, not exposed. The names "mount version" for 0x82/0x83, "created" for 0x90 and
/// "second time" for 0x98 are inferred. Nothing else is populated on the test images; the rest of the 448 bytes is zero.
///     </para>
/// </summary>
internal sealed record VolumeInformation
{
    public const ulong ObjectId = 0x500;
    public const ulong MirrorObjectId = 0x501;

    /// <summary>Length of the 0x520 row.</summary>
    public const int MetadataLength = 448;

    private const ushort LabelRow = 0x510;
    private const ushort MetadataRow = 0x520;
    private const ushort VerifyBlockRow = 0x540;
    private const int VerifyBlockLength = 16;

    /// <summary>Unpacks the values of the label (0x510), metadata (0x520) and verify block (0x540) rows; the label and the verify block may be empty.</summary>
    /// <exception cref="ReFsFormatException">The metadata row is missing (empty) or shorter than <see cref="MetadataLength" />.</exception>
    public VolumeInformation(ReadOnlySpan<byte> label, ReadOnlySpan<byte> metadata, ReadOnlySpan<byte> verifyBlock)
    {
        if (metadata.Length < MetadataLength)
        {
            throw new ReFsFormatException($"The volume information metadata row (key 0x520) is missing or too short ({metadata.Length} of {MetadataLength} bytes).");
        }

        Label = Encoding.Unicode.GetString(label);
        VolumeVersion = new Version(metadata[0x80], metadata[0x81]);
        DriverVersion = new Version(metadata[0x82], metadata[0x83]);
        Flags = (VolumeFlags)metadata.U32(0x84);
        Created = metadata.FileTime(0x90);
        SecondTime = metadata.FileTime(0x98);
        Mounted = metadata.FileTime(0xA0);

        if (verifyBlock.Length >= VerifyBlockLength)
        {
            VerifyBlockSector = verifyBlock.U64(0);
            VerifyBlockSectors = verifyBlock.U64(8);
        }
    }

    public string Label { get; }

    /// <summary>The on-disk format version (0x80, 0x81).</summary>
    public Version VolumeVersion { get; }

    /// <summary>The version stamped at mount by Windows (0x82, 0x83); the name is inferred.</summary>
    public Version DriverVersion { get; }

    public VolumeFlags Flags { get; }

    public DateTimeOffset Created { get; }

    public DateTimeOffset SecondTime { get; }

    public DateTimeOffset Mounted { get; }

    /// <summary>Row 0x540, u64 at 0: the sector Windows writes a copy of the metadata blob to at mount (0 when the row is absent).</summary>
    public ulong? VerifyBlockSector { get; }

    /// <summary>Row 0x540, u64 at 8: how many sectors that copy covers (0 when the row is absent).</summary>
    public ulong? VerifyBlockSectors { get; }

    /// <summary>
    /// Reads the table. Like Windows, which treats 0x500 and 0x501 as a failover pair (both must exist, or the mount fails), this needs both objects.
    /// It reads 0x500, and when that table cannot be read (a damaged
    /// page, including a checksum failure) it reads the partner 0x501. A readable table without its 0x520 row is not repaired from the partner:
    /// Windows fails the mount in that case.
    /// </summary>
    /// <exception cref="ReFsFormatException">An object of the pair is missing, the table has no 0x520 row, or that row is shorter than <see cref="MetadataLength" />.</exception>
    public static VolumeInformation Load(TreeReader tree, ObjectTable objects, ILogger logger)
    {
        if (!objects.TryGetRoot(ObjectId, out PageRef primary) || !objects.TryGetRoot(MirrorObjectId, out PageRef mirror))
        {
            throw new ReFsFormatException($"The volume information pair (0x{ObjectId:X}, 0x{MirrorObjectId:X}) is incomplete.");
        }

        ulong source = ObjectId;
        VolumeInformation information;

        try
        {
            information = Read(tree, primary);
        }
        catch (ReFsCorruptionException ex)
        {
            Logs.VolumeInformationFailover(logger, ObjectId, ex.Kind, ex.Lcn, MirrorObjectId);
            source = MirrorObjectId;
            information = Read(tree, mirror);
        }

        Logs.VolumeInformationRead(logger, source, information.Label, information.VolumeVersion, information.DriverVersion, information.Flags, information.Created, information.Mounted);
        return information;
    }

    /// <summary>
    ///     Reads one table of the pair. The row values are not copied: a span into a metadata page stays valid after the walk, because a page is its own
    ///     array (never pooled or reused), which the span keeps alive.
    /// </summary>
    private static VolumeInformation Read(TreeReader tree, PageRef root)
    {
        ReadOnlySpan<byte> label = default;
        ReadOnlySpan<byte> metadata = default;
        ReadOnlySpan<byte> verifyBlock = default;

        foreach (EntryView entry in tree.Rows(root))
        {
            if (entry.Key.Length != 8)
            {
                continue;
            }

            switch (entry.Key.U16(0))
            {
                case LabelRow:
                    label = entry.Value;
                    break;
                case MetadataRow:
                    metadata = entry.Value;
                    break;
                case VerifyBlockRow:
                    verifyBlock = entry.Value;
                    break;
            }
        }

        // A table without its metadata row leaves the span empty, which the constructor rejects like any other short row.
        return new VolumeInformation(label, metadata, verifyBlock);
    }
}