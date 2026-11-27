using DiscUtils.DeduplicationRole.Reparse.Enums;

namespace DiscUtils.DeduplicationRole.Reparse;

/// <summary>One entry of a property block's table: the value's type, size and offset from the block's signature.</summary>
internal readonly record struct DedupProperty
{
    public const int Length = 8;

    public DedupProperty(ReadOnlySpan<byte> entry)
    {
        Type = (DedupPropertyType)entry.U16(0);
        Size = entry.U16(2);
        Offset = entry.U32(4);
    }

    public DedupPropertyType Type { get; }

    public ushort Size { get; }

    public uint Offset { get; }
}
