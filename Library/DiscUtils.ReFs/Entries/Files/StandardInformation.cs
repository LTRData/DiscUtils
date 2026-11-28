using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries.Enums;

namespace DiscUtils.ReFs.Entries.Files;

/// <summary>
///     Standard information block of a file or directory record (the payload of its embedded table), common part: offsets 0x00 to 0x57, the same in every
///     layout. Names marked "inferred" are guesses; the offsets were checked against real images. Meanings of 0x40 to 0x54 and 0x58 to
///     0x74 below come from the forefst documentation (its page on <c>$STANDARD_INFORMATION</c>) checked against the 18 test images.
///     <para>
///         The layout grows with the format: <see cref="StandardInfoV34" /> (0x74 bytes, ReFS 3.4) adds 0x58 to 0x73, <see cref="StandardInfoV37" /> (0x7C bytes,
///         3.7 and later) adds 0x74. Windows picks by length and rejects a block shorter than 0x7C on a 3.7+ volume; a 3.4 volume's 0x74-byte
///         blocks are read with the 3.4 layout. <see cref="Parse" /> does the same when given the volume's minimum
///         (<see cref="BTree.TreeReader.StandardInformationMinimumLength" />).
///     </para>
///     <list type="table">
///         <listheader>
///             <term>Offset, size</term><description>Field</description>
///         </listheader>
///         <item>
///             <term>0x00, 8 each</term><description>Created, Modified, Changed, Accessed (FILETIME)</description>
///         </item>
///         <item>
///             <term>0x20, 4</term><description>FileAttributes (<see cref="ReFsAttributes" />)</description>
///         </item>
///         <item>
///             <term>0x24, 4</term>
///             <description>
///                 Internal flag word, <c>REFS_STD_INFO_INTERNAL</c>; 0 before ReFS 3.14. Bits 0 to 3 come from the file control block flags 0x8000000, 0x400000, 0x800 and
///                 0x80000000; bits 4 and 5 are a 2-bit value set at create.
///             </description>
///         </item>
///         <item>
///             <term>0x28, 8</term><description>SecurityId</description>
///         </item>
///         <item>
///             <term>0x30, 8</term>
///             <description>Length (logical size). Populated in a file's record (matches the stream size on every file of the 18 test images); 0 in a directory's own row.</description>
///         </item>
///         <item>
///             <term>0x38, 8</term><description>AllocatedLength, same remark</description>
///         </item>
///         <item>
///             <term>0x40, 8</term>
///             <description>LastUsn: the file's last record offset in the change journal. Written only when the file control block has flag 0x800000. 0 on all test images.</description>
///         </item>
///         <item>
///             <term>0x48, 8</term><description>UsnJournalId: a FILETIME naming the journal instance, written together with 0x40. 0 on all test images.</description>
///         </item>
///         <item>
///             <term>0x50, 4</term>
///             <description>
///                 PackedEaSize on ReFS 3.10 and later (on 3.4 to 3.9 the low half of the reparse tag). Non-zero on exactly the files with extended attributes in the EA test
///                 image.
///             </description>
///         </item>
///         <item>
///             <term>0x54, 4</term><description>ReparseTag (0 when none)</description>
///         </item>
///     </list>
/// </summary>
internal abstract record StandardInformation
{
    /// <summary>The shortest block any ReFS 3.x volume has (the ReFS 3.4 layout, 0x74 bytes).</summary>
    public const int MinimumLength = 116;

    /// <summary>The block's length in a ReFS 3.7 and later volume, and the shortest Windows accepts there.</summary>
    public const int Version37Length = 124;

    /// <summary>The format version from which a block must be at least <see cref="Version37Length" /> bytes.</summary>
    public static readonly Version Version37 = new Version(3, 7);

    /// <summary>Unpacks the common part (offsets 0 to 0x57) of a block.</summary>
    protected StandardInformation(ReadOnlySpan<byte> payload)
    {
        Created = payload.FileTime(0);
        Modified = payload.FileTime(8);
        Changed = payload.FileTime(16);
        Accessed = payload.FileTime(24);
        StoredAttributes = (ReFsAttributes)payload.U32(32);
        InternalFlags = (StandardInformationInternalFlags)payload.U32(36);
        SecurityId = payload.U64(40);
        Length = payload.U64(48);
        AllocatedLength = payload.U64(56);
        LastUsn = payload.U64(64);
        UsnJournalId = payload.U64(72);
        PackedEaSize = payload.U32(80);
        ReparseTag = payload.U32(84);
    }

    public DateTimeOffset Created { get; }

    public DateTimeOffset Modified { get; }

    public DateTimeOffset Changed { get; }

    public DateTimeOffset Accessed { get; }

    public ReFsAttributes StoredAttributes { get; }

    public StandardInformationInternalFlags InternalFlags { get; }

    public ulong SecurityId { get; }

    public ulong Length { get; }

    public ulong AllocatedLength { get; }

    public ulong LastUsn { get; }

    public ulong UsnJournalId { get; }

    public uint PackedEaSize { get; }

    public uint ReparseTag { get; }

    /// <summary>The attributes as Windows reports them: the stored word without the ReFS-only markers.</summary>
    public ReFsAttributes Attributes => StoredAttributes.WithoutMarkers();

    /// <summary>The shortest block a volume of <paramref name="formatVersion" /> may have.</summary>
    public static int MinimumLengthFor(Version formatVersion) => formatVersion >= Version37 ? Version37Length : MinimumLength;

    /// <summary>
    ///     Parses a block as the 3.7 layout when it is long enough, else as the 3.4 layout. A block shorter than <paramref name="minimumLength" /> (the volume's,
    ///     <see cref="MinimumLengthFor" />) is malformed.
    /// </summary>
    public static StandardInfoV34 Parse(ReadOnlySpan<byte> payload, int minimumLength = MinimumLength)
    {
        if (payload.Length < Math.Max(minimumLength, MinimumLength))
        {
            throw new ReFsFormatException($"Standard information too short ({payload.Length} bytes; this volume needs at least {Math.Max(minimumLength, MinimumLength)}).");
        }

        return payload.Length >= Version37Length ? new StandardInfoV37(payload) : new StandardInfoV34(payload);
    }
}