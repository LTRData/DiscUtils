using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.Mount;

/// <summary>
///     The common metadata page header: the first 0x50 bytes of every superblock ("SUPB"), checkpoint ("CHKP") and B+tree page ("MSB+"), the same in
///     every ReFS 3.x version. (Redo log pages, "MLog", have a different header.) The layout is derived from observed Windows behaviour and real images, with the two clock names
///     taken from the forefst documentation
///     (not independently confirmed).
///     <list type="table">
///         <listheader>
///             <term>Offset, size</term><description>Field</description>
///         </listheader>
///         <item>
///             <term>0x00, 4</term><description>Signature: "SUPB", "CHKP" or "MSB+"</description>
///         </item>
///         <item>
///             <term>0x04, 4</term><description>Header version, always 2 (Windows requires the byte at 4 to be 2 on a B+tree page; not enforced here)</description>
///         </item>
///         <item>
///             <term>0x08, 4</term><description>Reserved, 0 (not read)</description>
///         </item>
///         <item>
///             <term>0x0C, 4</term><description>Volume signature: the same value in every metadata structure of the volume (Windows compares it with the volume's)</description>
///         </item>
///         <item>
///             <term>0x10, 8</term><description>Virtual allocator clock: 0 in the superblock, counts up for checkpoints and B+tree pages</description>
///         </item>
///         <item>
///             <term>0x18, 8</term><description>Tree update clock: when the page's table was last changed</description>
///         </item>
///         <item>
///             <term>0x20, 8 each</term>
///             <description>
///                 The page's own location, four cluster numbers (Windows compares these 32 bytes with the reference the page was reached through). The superblock and the
///                 checkpoint use only the first.
///             </description>
///         </item>
///         <item>
///             <term>0x40, 8</term><description>Table id, high half: always 0</description>
///         </item>
///         <item>
///             <term>0x48, 8</term>
///             <description>Table id: the table a B+tree page belongs to (object table 2, container table 0xB, a directory's object id); 0 in the superblock and the checkpoint</description>
///         </item>
///     </list>
/// </summary>
internal abstract record MetadataPageHeader
{
    /// <summary>Length of the header.</summary>
    public const int HeaderLength = 0x50;

    /// <summary>A header with no values, for instances that are not read from a page (tests).</summary>
    protected MetadataPageHeader() { }

    /// <summary>Unpacks the header of a page. The signature is not checked here: each page type checks its own.</summary>
    /// <exception cref="ReFsFormatException">The page is shorter than the header.</exception>
    protected MetadataPageHeader(ReadOnlySpan<byte> page)
    {
        if (page.Length < HeaderLength)
        {
            throw new ReFsFormatException($"A metadata page of {page.Length} bytes is too short for its {HeaderLength}-byte header.");
        }

        Signature = page.U32(0x00);
        HeaderVersion = page.U32(0x04);
        VolumeSignature = page.U32(0x0C);
        VirtualAllocatorClock = page.U64(0x10);
        TreeUpdateClock = page.U64(0x18);
        Lcn0 = page.U64(0x20);
        Lcn1 = page.U64(0x28);
        Lcn2 = page.U64(0x30);
        Lcn3 = page.U64(0x38);
        TableIdHigh = page.U64(0x40);
        TableId = page.U64(0x48);
    }

    /// <summary>The four signature bytes as a little-endian number.</summary>
    public uint Signature { get; }

    public uint HeaderVersion { get; }

    /// <summary>Volume signature stored in every metadata structure (same value as in the superblock).</summary>
    public uint VolumeSignature { get; }

    public ulong VirtualAllocatorClock { get; }

    public ulong TreeUpdateClock { get; }

    /// <summary>The page's own first cluster.</summary>
    public ulong Lcn0 { get; }

    public ulong Lcn1 { get; }

    public ulong Lcn2 { get; }

    public ulong Lcn3 { get; }

    public ulong TableIdHigh { get; }

    /// <summary>Id of the table this page belongs to (object table = 2, container table = 0xB, directory = object id); 0 for a superblock or checkpoint.</summary>
    public ulong TableId { get; }
}