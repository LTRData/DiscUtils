using DiscUtils.Internal;
using DiscUtils.DeduplicationRole.Reparse.Enums;

namespace DiscUtils.DeduplicationRole.Reparse;

/// <summary>
///     A property block of Data Deduplication reparse data ("FeRp", "DdRp", "RbRp"); offsets are relative to the signature:
///     <code>
///     +0x00 u32  signature
///     +0x04 u32  CRC-32 of the block's bytes from +0x08 to its size
///     +0x08 u32  size of the block
///     +0x0C u16  0
///     +0x0E u16  number of properties
///     +0x10      {u16 type, u16 size, u32 offset}[number]; a property's index is its meaning (see the block types)
///     </code>
/// </summary>
internal sealed class DedupPropertyBlock
{
    private const int HeaderLength = 0x10;
    private const int PropertyLength = 8;
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly DedupProperty[] _properties;

    /// <summary>Parses the block at the start of <paramref name="bytes" /> and checks its signature, size, CRC-32 and property bounds.</summary>
    public DedupPropertyBlock(ReadOnlyMemory<byte> bytes, uint signature, string name)
    {
        ReadOnlySpan<byte> block = bytes.Span;
        if (block.Length < HeaderLength || block.U32(0) != signature)
            throw new DedupFormatException($"Data Deduplication {name} block not found.");

        uint size = block.U32(8);
        if (size < HeaderLength || size > block.Length || Crc32LittleEndian.Compute(Crc32Algorithm.Common, block[8..(int)size]) != block.U32(4))
            throw new DedupFormatException($"Data Deduplication {name} block is damaged.");

        int count = block.U16(0xE);
        if (HeaderLength + count * PropertyLength > size)
            throw new DedupFormatException($"Data Deduplication {name} block lists more properties than it holds.");

        _bytes = bytes[..(int)size];
        _properties = new DedupProperty[count];
        for (int i = 0; i < count; i++)
        {
            DedupProperty property = new DedupProperty(block.Slice(HeaderLength + i * PropertyLength, PropertyLength));
            if (property.Size != 0 && (property.Offset < HeaderLength || property.Offset + (ulong)property.Size > size))
                throw new DedupFormatException($"Data Deduplication {name} property {i} lies outside its block.");

            _properties[i] = property;
        }
    }

    public int Count => _properties.Length;

    /// <summary>Whether the block has a non-empty property at <paramref name="index" />.</summary>
    public bool Has(int index)
    {
        return index < _properties.Length && _properties[index].Size != 0;
    }

    /// <summary>The value of the property at <paramref name="index" />, which must have this type (and this size, when given).</summary>
    public ReadOnlyMemory<byte> Get(int index, DedupPropertyType type, int? size = null)
    {
        if (index >= _properties.Length || _properties[index].Type != type || (size is { } expected && _properties[index].Size != expected))
            throw new DedupFormatException($"Data Deduplication reparse property {index} is missing or is not a {(size is null ? string.Empty : $"{size}-byte ")}{type}.");

        DedupProperty property = _properties[index];
        return property.Size == 0 ? ReadOnlyMemory<byte>.Empty : _bytes.Slice((int)property.Offset, property.Size);
    }
}