using DiscUtils.ReFs.BTree.Enums;
using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.BTree;

/// <summary>
///     A node entry. Normal form: {u32 size, u16 keyOff, u16 keyLen, u16 flags, u16 valOff, u32 valLen}.
///     Compact form (flags 0x40): value is the entry itself (size u16 @10), key is the 12 bytes at +12.
/// </summary>
internal readonly ref struct EntryView
{
    internal EntryView(ReadOnlySpan<byte> node, int offset)
    {
        if (offset < 0 || offset > node.Length - 16)
        {
            throw new ReFsFormatException("Entry header out of bounds.");
        }

        ReadOnlySpan<byte> entry = node[offset..];
        Flags = (EntryProperties)entry.U16(8);

        // Bitwise test avoids Enum.HasFlag boxing in Debug builds on this zero-allocation hot path.
        if ((Flags & EntryProperties.Compact) != 0)
        {
            int size = entry.U16(10);
            if (size < 24 || size > entry.Length)
            {
                throw new ReFsFormatException("Compact entry out of bounds.");
            }

            Key = entry.Slice(12, 12);
            Value = entry[..size];
            return;
        }

        uint declaredSize = entry.U32(0);
        if (declaredSize < 16 || declaredSize > entry.Length)
        {
            throw new ReFsFormatException("Normal entry out of bounds.");
        }

        entry = entry[..(int)declaredSize];
        int keyOffset = entry.U16(4);
        int keyLength = entry.U16(6);
        int valueOffset = entry.U16(10);
        long valueLength = entry.U32(12);
        if (keyOffset + keyLength > entry.Length || valueOffset + valueLength > entry.Length)
        {
            throw new ReFsFormatException("Entry key/value out of bounds.");
        }

        Key = entry.Slice(keyOffset, keyLength);
        Value = entry.Slice(valueOffset, (int)valueLength);
    }

    public EntryProperties Flags { get; }

    public ReadOnlySpan<byte> Key { get; }

    public ReadOnlySpan<byte> Value { get; }

    public bool IsCompact => Flags.HasFlag(EntryProperties.Compact);

    /// <summary>The value is itself an embedded table (file records, stream run tables).</summary>
    public bool IsEmbeddedTable => !IsCompact && Flags.HasFlag(EntryProperties.EmbeddedTable);
}