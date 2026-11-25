using DiscUtils.Streams;

namespace DiscUtils.DeduplicationRole;

/// <summary>Little-endian reads at an offset of a span.</summary>
internal static class SpanReadExtensions
{
    extension(ReadOnlySpan<byte> span)
    {
        public ushort U16(int offset) => EndianUtilities.ToUInt16LittleEndian(span.Slice(offset));

        public uint U32(int offset) => EndianUtilities.ToUInt32LittleEndian(span.Slice(offset));

        public ulong U64(int offset) => EndianUtilities.ToUInt64LittleEndian(span.Slice(offset));
    }
}