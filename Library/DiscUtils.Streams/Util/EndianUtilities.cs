//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using LTRData.Extensions.Buffers;
using LTRData.Extensions.Split;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Streams;

public static class EndianUtilities
{
    #region Bit Twiddling

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(ushort val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(ushort val, Span<byte> buffer)
        => BinaryPrimitives.WriteUInt16LittleEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(uint val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(uint val, Span<byte> buffer)
        => BinaryPrimitives.WriteUInt32LittleEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(ulong val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(ulong val, Span<byte> buffer)
        => BinaryPrimitives.WriteUInt64LittleEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(short val, Span<byte> buffer)
        => BinaryPrimitives.WriteInt16LittleEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(short val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(int val, Span<byte> buffer)
        => BinaryPrimitives.WriteInt32LittleEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(int val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(long val, Span<byte> buffer)
        => BinaryPrimitives.WriteInt64LittleEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(long val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(Guid val, Span<byte> buffer)
    {
#if NET8_0_OR_GREATER
        MemoryMarshal.Write(buffer, val);
#else
        MemoryMarshal.Write(buffer, ref val);
#endif

        if (!BitConverter.IsLittleEndian)
        {
            Unsafe.As<byte, uint>(ref buffer[0]) = BinaryPrimitives.ReverseEndianness(Unsafe.As<byte, uint>(ref buffer[0]));
            Unsafe.As<byte, ushort>(ref buffer[4]) = BinaryPrimitives.ReverseEndianness(Unsafe.As<byte, ushort>(ref buffer[4]));
            Unsafe.As<byte, ushort>(ref buffer[6]) = BinaryPrimitives.ReverseEndianness(Unsafe.As<byte, ushort>(ref buffer[6]));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesLittleEndian(Guid val, byte[] buffer, int offset)
        => WriteBytesLittleEndian(val, buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(ushort val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(ushort val, Span<byte> buffer)
        => BinaryPrimitives.WriteUInt16BigEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(uint val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(uint val, Span<byte> buffer)
        => BinaryPrimitives.WriteUInt32BigEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(ulong val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteUInt64BigEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(ulong val, Span<byte> buffer)
        => BinaryPrimitives.WriteUInt64BigEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(short val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(short val, Span<byte> buffer)
        => BinaryPrimitives.WriteInt16BigEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(int val, Span<byte> buffer)
        => BinaryPrimitives.WriteInt32BigEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(int val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(long val, Span<byte> buffer)
        => BinaryPrimitives.WriteInt64BigEndian(buffer, val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(long val, byte[] buffer, int offset)
        => BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset), val);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(Guid val, byte[] buffer, int offset)
        => WriteBytesBigEndian(val, buffer.AsSpan(offset, 16));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytesBigEndian(Guid val, Span<byte> buffer)
    {
#if NET8_0_OR_GREATER
        MemoryMarshal.Write(buffer, val);
#else
        MemoryMarshal.Write(buffer, ref val);
#endif

        if (BitConverter.IsLittleEndian)
        {
            Unsafe.As<byte, uint>(ref buffer[0]) = BinaryPrimitives.ReverseEndianness(Unsafe.As<byte, uint>(ref buffer[0]));
            Unsafe.As<byte, ushort>(ref buffer[4]) = BinaryPrimitives.ReverseEndianness(Unsafe.As<byte, ushort>(ref buffer[4]));
            Unsafe.As<byte, ushort>(ref buffer[6]) = BinaryPrimitives.ReverseEndianness(Unsafe.As<byte, ushort>(ref buffer[6]));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ToUInt16LittleEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ToUInt16LittleEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadUInt16LittleEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToUInt32LittleEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToUInt32LittleEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadUInt32LittleEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUInt32LittleEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        stream.ReadExactly(buffer);
        return ToUInt32LittleEndian(buffer);
    }

    public static async ValueTask<uint> ReadUInt32LittleEndianAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(sizeof(uint));
        try
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            return ToUInt32LittleEndian(buffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToUInt64LittleEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToUInt64LittleEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadUInt64LittleEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short ToInt16LittleEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short ToInt16LittleEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadInt16LittleEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt32LittleEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt32LittleEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadInt32LittleEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadInt32LittleEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        stream.ReadExactly(buffer);
        return ToInt32LittleEndian(buffer);
    }

    public static async ValueTask<int> ReadInt32LittleEndianAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(sizeof(int));
        try
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            return ToInt32LittleEndian(buffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ToInt64LittleEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ToInt64LittleEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadInt64LittleEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ToUInt16BigEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ToUInt16BigEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadUInt16BigEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToUInt32BigEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToUInt32BigEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadUInt32BigEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToUInt64BigEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadUInt64BigEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToUInt64BigEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadUInt64BigEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short ToInt16BigEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short ToInt16BigEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadInt16BigEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt32BigEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt32BigEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadInt32BigEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ToInt64BigEndian(byte[] buffer, int offset)
        => BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ToInt64BigEndian(ReadOnlySpan<byte> buffer)
        => BinaryPrimitives.ReadInt64BigEndian(buffer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid ToGuidLittleEndian(byte[] buffer, int offset) =>
        ToGuidLittleEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid ToGuidLittleEndian(ReadOnlySpan<byte> buffer)
    {
        if (BitConverter.IsLittleEndian)
        {
            return MemoryMarshal.Read<Guid>(buffer);
        }
        else
        {
            return new Guid(
                ToUInt32LittleEndian(buffer[..4]),
                ToUInt16LittleEndian(buffer.Slice(4, 2)),
                ToUInt16LittleEndian(buffer.Slice(6, 2)),
                buffer[8],
                buffer[9],
                buffer[10],
                buffer[11],
                buffer[12],
                buffer[13],
                buffer[14],
                buffer[15]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid ToGuidBigEndian(byte[] buffer, int offset) =>
        ToGuidBigEndian(buffer.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid ToGuidBigEndian(ReadOnlySpan<byte> buffer)
    {
        if (!BitConverter.IsLittleEndian)
        {
            return MemoryMarshal.Read<Guid>(buffer);
        }
        else
        {
            return new Guid(
                ToUInt32BigEndian(buffer[..4]),
                ToUInt16BigEndian(buffer.Slice(4, 2)),
                ToUInt16BigEndian(buffer.Slice(6, 2)),
                buffer[8],
                buffer[9],
                buffer[10],
                buffer[11],
                buffer[12],
                buffer[13],
                buffer[14],
                buffer[15]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] ToByteArray(ReadOnlySpan<byte> buffer)
        => buffer.ToArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ToStruct<T>(byte[] buffer, int offset)
        where T : IByteArraySerializable, new()
    {
        var result = new T();
        result.ReadFrom(buffer.AsSpan(offset));
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ToStruct<T>(ReadOnlySpan<byte> buffer)
        where T : IByteArraySerializable, new()
    {
        var result = new T();
        result.ReadFrom(buffer);
        return result;
    }

    public static string[] LittleEndianUnicodeBytesToStringArray(ReadOnlySpan<byte> bytes)
    {
        var chars = MemoryMarshal.Cast<byte, char>(bytes);

        if (chars.Length <= 2)
        {
            return [];
        }

        var endpos = chars.IndexOf("\0\0");

        if (endpos >= 0)
        {
            chars = chars[..endpos];
        }

#if NET8_0_OR_GREATER
        var count = chars.Count('\0') + 1;
#else
        var count = 1;

        foreach (var c in chars)
        {
            if (c == '\0')
            {
                count++;
            }
        }
#endif

        var array = new string[count];

        var i = 0;

        foreach (var str in chars.TokenEnum('\0'))
        {
            if (!BitConverter.IsLittleEndian)
            {
                array[i++] = Encoding.Unicode.GetString(MemoryMarshal.Cast<char, byte>(str));
            }
            else
            {
                array[i++] = str.ToString();
            }
        }

        return array;
    }

    public static string LittleEndianUnicodeBytesToString(ReadOnlySpan<byte> bytes)
    {
        if (!BitConverter.IsLittleEndian)
        {
            return Encoding.Unicode.GetString(MemoryMarshal.Cast<char, byte>(bytes.ReadNullTerminatedUnicode()));
        }

        return MemoryMarshal.Cast<byte, char>(bytes).ReadNullTerminatedUnicodeString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string LittleEndianUnicodeBytesToString(byte[] bytes, int offset, int count)
        => LittleEndianUnicodeBytesToString(bytes.AsSpan(offset, count));

    public static ReadOnlySpan<byte> StringToLittleEndianUnicodeBytes(ReadOnlySpan<char> chars)
    {
        if (!BitConverter.IsLittleEndian)
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
            var byteCount = Encoding.Unicode.GetByteCount(chars);
            var bytes = StreamUtilities.GetUninitializedArray<byte>(byteCount);
            return bytes.AsSpan(0, Encoding.Unicode.GetBytes(chars, bytes));
#else
            var buffer = ArrayPool<char>.Shared.Rent(chars.Length);
            try
            {
                chars.CopyTo(buffer);
                return Encoding.Unicode.GetBytes(buffer, 0, chars.Length);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
#endif
        }

        return MemoryMarshal.AsBytes(chars);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> StringToLittleEndianUnicodeBytes(string chars)
    {
        if (!BitConverter.IsLittleEndian)
        {
            return Encoding.Unicode.GetBytes(chars);
        }

        return MemoryMarshal.AsBytes(chars.AsSpan());
    }

    /// <summary>
    /// Primitive conversion from Latin1 to Unicode that stops at a null-terminator.
    /// </summary>
    /// <param name="data">The data to convert.</param>
    /// <param name="offset">The first byte to convert.</param>
    /// <param name="count">The number of bytes to convert.</param>
    /// <returns>The string.</returns>
    /// <remarks>The built-in ASCIIEncoding converts characters of codepoint > 127 to ?,
    /// this preserves those code points.</remarks>
    public static string BytesToZString(byte[] data, int offset, int count)
    {
        var z = Array.IndexOf(data, default, offset, Math.Min(count, data.Length - offset));

        if (z >= 0)
        {
            count = z - offset;
        }

        return EncodingUtilities.GetLatin1Encoding().GetString(data, offset, count);
    }

    /// <summary>
    /// Primitive conversion from Latin1 to Unicode that stops at a null-terminator.
    /// </summary>
    /// <param name="data">The data to convert.</param>
    /// <returns>The string.</returns>
    /// <remarks>The built-in ASCIIEncoding converts characters of codepoint > 127 to ?,
    /// this preserves those code points.</remarks>
    public static string BytesToZString(ReadOnlySpan<byte> data)
    {
        var z = data.IndexOf(default(byte));

        if (z >= 0)
        {
            data = data[..z];
        }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        return EncodingUtilities.GetLatin1Encoding().GetString(data);
#else
        var buffer = ArrayPool<byte>.Shared.Rent(data.Length);
        try
        {
            data.CopyTo(buffer);
            return EncodingUtilities.GetLatin1Encoding().GetString(buffer, 0, data.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
#endif
    }

#endregion
}
