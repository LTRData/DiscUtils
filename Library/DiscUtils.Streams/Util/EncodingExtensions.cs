using System.Text;
using System.Buffers;
using System;

namespace DiscUtils.Streams;

public static class EncodingExtensions
{
    public static int GetBytes(this Encoding encoding, string chars, Span<byte> bytes)
    {
        return encoding.GetBytes(chars.AsSpan(), bytes);
    }

#if !NETSTANDARD2_1_OR_GREATER && !NETCOREAPP
    public static int GetBytes(this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        var str = ArrayPool<char>.Shared.Rent(chars.Length);
        try
        {
            chars.CopyTo(str);
            var buffer = ArrayPool<byte>.Shared.Rent(encoding.GetByteCount(str, 0, chars.Length));
            try
            {
                var length = encoding.GetBytes(str, 0, chars.Length, buffer, 0);
                buffer.AsSpan(0, length).CopyTo(bytes);
                return length;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(str);
        }
    }
#endif

#if !NETSTANDARD2_1_OR_GREATER && !NETCOREAPP
    public static string GetString(this Encoding encoding, ReadOnlySpan<byte> bytes)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bytes.Length);
        try
        {
            bytes.CopyTo(buffer);
            return encoding.GetString(buffer, 0, bytes.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
#endif
}
