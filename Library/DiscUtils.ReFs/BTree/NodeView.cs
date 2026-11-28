using DiscUtils.ReFs.BTree.Enums;
using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.BTree;

/// <summary>
/// View of a B+tree node inside a buffer (a metadata page or an embedded table value).
/// Header, 0x28 bytes, as observed on the test images: 0x00 u32 data start (0x28 in a new node); 0x04 u32 data end; 0x08 u32 free bytes; 0x0C u8 level (0 = leaf); 0x0D u8 flags (<see
/// cref="NodeProperties" />); 0x0E u16 zero (unknown); 0x10 u32 offset of the slot array (a new node has the node size here: the array grows down
/// from the end); 0x14 u32 entry count; 0x18 the base of the slot key index (written as a u64, read as a u16); 0x20 u32 node size; 0x24 u32 zero
/// (unknown). Slots are {u16 entry offset, u16 start value minus that base}; the second u16 is meaningful only with <see
/// cref="NodeProperties.KeyIndexValid" /> and is not read here.
/// </summary>
internal readonly ref struct NodeView
{
    public const int HeaderSize = 0x28;

    private readonly ReadOnlySpan<byte> _node;
    private readonly int _slotOffset;
    private readonly int _dataStart;
    private readonly int _dataEnd;

    public NodeView(ReadOnlySpan<byte> buffer, int nodeOffset)
    {
        if (nodeOffset < 0 || nodeOffset > buffer.Length - HeaderSize)
        {
            throw new ReFsFormatException("Node header out of bounds.");
        }

        uint size = buffer[nodeOffset..].U32(32);
        if (size < HeaderSize || size > buffer.Length - nodeOffset)
        {
            throw new ReFsFormatException("Declared node extent out of bounds.");
        }

        _node = buffer.Slice(nodeOffset, (int)size);
        Height = _node[12];
        Flags = (NodeProperties)_node[13];
        _dataStart = (int)_node.U32(0);
        _dataEnd = (int)_node.U32(4);
        _slotOffset = (int)_node.U32(16);
        Count = (int)_node.U32(20);

        if (_dataStart < HeaderSize || _dataEnd < _dataStart || _dataEnd > _slotOffset)
        {
            throw new ReFsFormatException("Node data extent out of bounds.");
        }

        if (Count < 0 || _slotOffset < HeaderSize || _slotOffset + 4L * Count > _node.Length)
        {
            throw new ReFsFormatException("Node slot array out of bounds.");
        }
    }

    /// <summary>Distance from the leaf level; 0 for leaf nodes (rows), greater for branch nodes (child pointers).</summary>
    public byte Height { get; }

    /// <summary>u8 @0x0D.</summary>
    public NodeProperties Flags { get; }

    public int Count { get; }

    public bool IsLeaf => Height == 0;

    public EntryView this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            uint slot = _node.U32(_slotOffset + 4 * index);
            // Bitwise test avoids Enum.HasFlag boxing in Debug builds on this zero-allocation hot path.
            bool wide = (Flags & NodeProperties.WideSlots) != 0;
            if (wide && slot > int.MaxValue)
            {
                throw new ReFsFormatException("Entry header out of bounds.");
            }

            int entryOffset = wide ? (int)slot : (int)(slot & 0xFFFF);
            if (entryOffset < _dataStart)
            {
                throw new ReFsFormatException("Node entry precedes the data extent.");
            }

            return new EntryView(_node[.._dataEnd], entryOffset);
        }
    }
}