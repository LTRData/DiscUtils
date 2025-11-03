using System;

namespace DiscUtils.Streams;

public static class BufferUtilities
{
    /// <summary>
    /// Indicates if two ranges overlap.
    /// </summary>
    /// <typeparam name="T">The type of the ordinals.</typeparam>
    /// <param name="xFirst">The lowest ordinal of the first range (inclusive).</param>
    /// <param name="xLast">The highest ordinal of the first range (exclusive).</param>
    /// <param name="yFirst">The lowest ordinal of the second range (inclusive).</param>
    /// <param name="yLast">The highest ordinal of the second range (exclusive).</param>
    /// <returns><c>true</c> if the ranges overlap, else <c>false</c>.</returns>
    public static bool RangesOverlap<T>(T xFirst, T xLast, T yFirst, T yLast) where T : IComparable<T>
    {
        return !((xLast.CompareTo(yFirst) <= 0) || (xFirst.CompareTo(yLast) >= 0));
    }

    public static bool IsAllZeros(byte[] buffer, int offset, int count)
    {
#if NET8_0_OR_GREATER
        return !buffer.AsSpan(offset, count).ContainsAnyExcept((byte)0);
#else
        var end = offset + count;
        for (var i = offset; i < end; ++i)
        {
            if (buffer[i] != 0)
            {
                return false;
            }
        }

        return true;
#endif
    }

    public static bool IsAllZeros(ReadOnlySpan<byte> buffer)
    {
#if NET8_0_OR_GREATER
        return !buffer.ContainsAnyExcept((byte)0);
#else
        for (var i = 0; i < buffer.Length; ++i)
        {
            if (buffer[i] != 0)
            {
                return false;
            }
        }

        return true;
#endif
    }

    public static bool AreEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        if (ReferenceEquals(a, b) || a.SequenceEqual(b))
        {
            return true;
        }

        return false;
    }

    public static ushort BitSwap(ushort value)
    {
        return (ushort)(((value & 0x00FF) << 8) | ((value & 0xFF00) >> 8));
    }

    public static uint BitSwap(uint value)
    {
        return ((value & 0xFF) << 24) | ((value & 0xFF00) << 8) | ((value & 0x00FF0000) >> 8) |
               ((value & 0xFF000000) >> 24);
    }

    public static ulong BitSwap(ulong value)
    {
        return ((ulong)BitSwap((uint)(value & 0xFFFFFFFF)) << 32) | BitSwap((uint)(value >> 32));
    }

    public static short BitSwap(short value)
    {
        return (short)BitSwap((ushort)value);
    }

    public static int BitSwap(int value)
    {
        return (int)BitSwap((uint)value);
    }

    public static long BitSwap(long value)
    {
        return (long)BitSwap((ulong)value);
    }
}
