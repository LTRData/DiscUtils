using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.BTree;

/// <summary>
/// The index root: the descriptor in front of a B+tree node, at 0x50 of every "MSB+" page and at offset 0 of every embedded table (a row value that
/// is itself a table). It describes the table; the node header after it describes one node. The layout is inferred from observed
/// behaviour of Windows and from the test images (for the two counts).
///     <list type="table">
///         <listheader>
///             <term>Offset, size</term><description>Field</description>
///         </listheader>
///         <item>
///             <term>0x00, 4</term><description>Offset from here to the node header: 0x28 plus the table's payload size (from its definition)</description>
///         </item>
///         <item>
///             <term>0x04, 2</term>
///             <description>
///                 Length of this descriptor, 0x28. A page that is not the root of its table has only the first 8 bytes ({u32 8, u32 0}): this is 0 there and the fields
///                 below are absent.
///             </description>
///         </item>
///         <item>
///             <term>0x06, 2</term>
///             <description>A u16 of the table definition: 0 on system tables, 0xE010 on the allocator tables, 1 on directories and embedded tables. Meaning unknown; not read.</description>
///         </item>
///         <item>
///             <term>0x08, 2</term><description>Flags; bit 1 is set on every table seen. Not read. 0x0A u16 zero.</description>
///         </item>
///         <item>
///             <term>0x0C, 4</term><description>Schema id (0xE030 object table, 0xE0C0 container table, 0x130 directory, 0x1E0 file record, 0x200 run table)</description>
///         </item>
///         <item>
///             <term>0x10, 4</term>
///             <description>
///                 A second schema id from the table definition; equal to the first on every table seen.
///                 Not read.
///             </description>
///         </item>
///         <item>
///             <term>0x14, 2 and 0x16, 2</term><description>Two u16 of the table object (2 and 0 on every table seen). Meaning unknown; not read.</description>
///         </item>
///         <item>
///             <term>0x18, 8</term>
///             <description>
///                 Number of pages under the root, interior ones included (so the table's pages minus the root); 0 when the root is itself the leaf. From the images (it
///                 equals the counted pages on every table, including one with two interior levels: 333 leaves and 3 interior pages give 336).
///             </description>
///         </item>
///         <item>
///             <term>0x20, 8</term>
///             <description>Number of rows in the whole table. From the images (it equals the counted rows on every table).</description>
///         </item>
///     </list>
///     The table's payload (for a file record, the standard information) lies between the descriptor and the node header.
/// </summary>
internal readonly record struct IndexRoot
{
    /// <summary>Length of the full descriptor.</summary>
    public const int DescriptorLength = 0x28;

    /// <summary>What every page has, root or not: the node offset and the descriptor length.</summary>
    public const int MinimumLength = 8;

    /// <summary>Unpacks the index root at the start of <paramref name="bytes" />.</summary>
    /// <exception cref="ReFsFormatException">The bytes are too short, or the descriptor does not fit before the node.</exception>
    public IndexRoot(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < MinimumLength)
        {
            throw new ReFsFormatException($"Index root too short ({bytes.Length} bytes).");
        }

        NodeOffset = bytes.U32(0x00);
        Length = bytes.U16(0x04);

        if (Length == 0)
        {
            return; // not the root of its table: only the node offset
        }

        if (Length < DescriptorLength || Length > NodeOffset || bytes.Length < DescriptorLength)
        {
            throw new ReFsFormatException($"Index root descriptor out of range (length 0x{Length:X}, node offset 0x{NodeOffset:X}).");
        }

        SchemaId = bytes.U32(0x0C);
        PagesBelowRoot = bytes.U64(0x18);
        RowCount = bytes.U64(0x20);
    }

    /// <summary>u32 @0x00: offset of the node header from the start of this structure.</summary>
    public uint NodeOffset { get; }

    /// <summary>u16 @0x04: length of the descriptor (where the table's payload starts); 0 on a page that is not the root of its table.</summary>
    public ushort Length { get; }

    /// <summary>u32 @0x0C; 0 without a descriptor.</summary>
    public uint SchemaId { get; }

    /// <summary>u64 @0x18: the pages under the root, leaf and interior; 0 when the root is the only node, or without a descriptor.</summary>
    public ulong PagesBelowRoot { get; }

    /// <summary>u64 @0x20: rows in the whole table; 0 without a descriptor.</summary>
    public ulong RowCount { get; }

    /// <summary>True for the root of a table (a root page or an embedded table), which carries the descriptor.</summary>
    public bool HasDescriptor => Length != 0;
}