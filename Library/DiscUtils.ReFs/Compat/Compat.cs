// Polyfills for BCL APIs missing on the older target frameworks. Members that exist on a
// framework always win over these extensions, so they are only compiled where needed.

#if !NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
using System.Text;

namespace DiscUtils.ReFs.Compat;

internal static class Compat
{
    extension(Encoding encoding)
    {
        public string GetString(ReadOnlySpan<byte> bytes) => bytes.IsEmpty ? string.Empty : encoding.GetString(bytes.ToArray());
    }

    extension(Encoding)
    {
        public static Encoding Latin1 => Encoding.GetEncoding(28591);
    }

    extension(Convert)
    {
        public static string ToHexString(ReadOnlySpan<byte> bytes)
        {
            var chars = new char[bytes.Length * 2];

            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = "0123456789ABCDEF"[bytes[i] >> 4];
                chars[(i * 2) + 1] = "0123456789ABCDEF"[bytes[i] & 0xF];
            }

            return new string(chars);
        }
    }

    extension(BitConverter)
    {
        public static bool TryWriteBytes(Span<byte> destination, uint value)
        {
            if (destination.Length < sizeof(uint))
            {
                return false;
            }

            for (var i = 0; i < sizeof(uint); i++)
            {
                destination[i] = (byte)(value >> (8 * i));
            }

            return true;
        }

        public static bool TryWriteBytes(Span<byte> destination, ulong value)
        {
            if (destination.Length < sizeof(ulong))
            {
                return false;
            }

            for (var i = 0; i < sizeof(ulong); i++)
            {
                destination[i] = (byte)(value >> (8 * i));
            }

            return true;
        }

        public static bool TryWriteBytes(Span<byte> destination, ushort value)
        {
            if (destination.Length < sizeof(ushort))
            {
                return false;
            }

            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            return true;
        }
    }

    extension(uint)
    {
        public static bool IsPow2(uint value) => value != 0 && (value & (value - 1)) == 0;
    }

    extension(ArgumentNullException)
    {
        public static void ThrowIfNull(object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null)
            {
                throw new ArgumentNullException(paramName);
            }
        }
    }

    extension(ArgumentOutOfRangeException)
    {
        public static void ThrowIfNegative(long value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(paramName);
            }
        }

        public static void ThrowIfGreaterThanOrEqual(long value, long other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (value >= other)
            {
                throw new ArgumentOutOfRangeException(paramName);
            }
        }
    }

    extension<TKey, TValue>(KeyValuePair<TKey, TValue> pair)
    {
        public void Deconstruct(out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }
    }

    extension<TKey, TValue>(Dictionary<TKey, TValue> dictionary) where TKey : notnull
    {
        public bool TryAdd(TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key))
            {
                return false;
            }

            dictionary.Add(key, value);
            return true;
        }

        public TValue? GetValueOrDefault(TKey key) => dictionary.TryGetValue(key, out var value) ? value : default;
    }
}
#endif