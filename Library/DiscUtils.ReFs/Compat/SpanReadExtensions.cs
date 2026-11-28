using System.Runtime.CompilerServices;
using DiscUtils.Streams;

namespace DiscUtils.ReFs.Compat;

/// <summary>Little-endian field readers for on-disk structures, built on <see cref="EndianUtilities" />.</summary>
internal static class SpanReadExtensions
{
    private const ulong MaxFileTime = 2_650_467_743_999_999_999UL; // 9999-12-31T23:59:59.9999999Z

    extension(ReadOnlySpan<byte> span)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort U16(int offset) => EndianUtilities.ToUInt16LittleEndian(span.Slice(offset));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint U32(int offset) => EndianUtilities.ToUInt32LittleEndian(span.Slice(offset));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong U64(int offset) => EndianUtilities.ToUInt64LittleEndian(span.Slice(offset));

        /// <summary>Reads a Windows FILETIME (100 ns ticks since 1601-01-01 UTC); out-of-range values are clamped.</summary>
        public DateTimeOffset FileTime(int offset) => new DateTimeOffset(DateTime.FromFileTimeUtc((long)Math.Min(span.U64(offset), MaxFileTime)), TimeSpan.Zero);
    }
}