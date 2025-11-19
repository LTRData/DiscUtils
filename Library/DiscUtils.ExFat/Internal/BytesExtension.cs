// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;

namespace DiscUtils.ExFat.Internal;

/// <summary>
/// Extenstions to <see cref="byte"/>[]
/// </summary>
public static class BytesExtension
{
    /// <summary>
    /// Writes the specified bytes to a buffer, clearing any remaining
    /// bytes in target buffer.
    /// </summary>
    /// <param name="memory">Memory buffer to modify</param>
    /// <param name="bytes">The new bytes.</param>
    /// <exception cref="ArgumentException">bytes</exception>
    public static void Set(this Memory<byte> memory, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(memory.Span);
        memory.Span.Slice(bytes.Length).Clear();
    }

    /// <summary>
    /// Gets the checksum of the given buffer.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="checksum">The checksum.</param>
    /// <returns></returns>
    public static ushort GetChecksum16(this Span<byte> bytes, ushort checksum = 0)
        => GetChecksum16((ReadOnlySpan<byte>)bytes, checksum);

    /// <summary>
    /// Gets the checksum of the given buffer.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="checksum">The checksum.</param>
    /// <returns></returns>
    public static ushort GetChecksum16(this ReadOnlySpan<byte> bytes, ushort checksum = 0)
    {
        foreach (var b in bytes)
        {
            checksum = (ushort)(checksum.RotateRight() + b);
        }

        return checksum;
    }

    /// <summary>
    /// Gets the checksum of the given buffer.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="count">The count.</param>
    /// <param name="checksum">The checksum.</param>
    /// <returns></returns>
    public static uint GetChecksum32(this byte[] bytes, int offset, int count, uint checksum = 0)
    {
        count += offset;
        for (var index = offset; index < count; index++)
        {
            checksum = checksum.RotateRight() + bytes[index];
        }

        return checksum;
    }
}
