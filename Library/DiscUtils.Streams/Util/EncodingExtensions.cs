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

    public static int GetCharCount(this Decoder decoder, ReadOnlySpan<byte> bytes, bool flush)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bytes.Length);
        try
        {
            bytes.CopyTo(buffer);
            return decoder.GetCharCount(buffer, 0, bytes.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static int GetChars(this Decoder decoder, ReadOnlySpan<byte> bytes, Span<char> chars, bool flush)
    {
        var bytesBuffer = ArrayPool<byte>.Shared.Rent(bytes.Length);
        try
        {
            var charsBuffer = ArrayPool<char>.Shared.Rent(chars.Length);
            try
            {
                bytes.CopyTo(bytesBuffer);
                var i = decoder.GetChars(bytesBuffer, 0, bytes.Length, charsBuffer, 0);
                charsBuffer.AsSpan(0, i).CopyTo(chars);
                return i;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(charsBuffer);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytesBuffer);
        }
    }

    public static void Convert(this Encoder decoder, ReadOnlySpan<char> chars, Span<byte> bytes, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
    {
        var bytesBuffer = ArrayPool<byte>.Shared.Rent(bytes.Length);
        try
        {
            var charsBuffer = ArrayPool<char>.Shared.Rent(chars.Length);
            try
            {
                chars.CopyTo(charsBuffer);
                decoder.Convert(charsBuffer, 0, chars.Length, bytesBuffer, 0, bytes.Length, flush, out charsUsed, out bytesUsed, out completed);
                bytesBuffer.AsSpan(0, bytesUsed).CopyTo(bytes);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(charsBuffer);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytesBuffer);
        }
    }

#endif
}
