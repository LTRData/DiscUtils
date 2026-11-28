using System.Security.Cryptography;

namespace DiscUtils.ReFs.Checksums;

/// <summary>
///     XXH64, SHA-256 and bit counting, available on every target framework. CRC-32C and CRC-64 come from DiscUtils.Core
///     (<c>Crc32LittleEndian</c>, <c>Crc64LittleEndian</c>).
/// </summary>
internal static class Hashing
{
    private const ulong P1 = 11400714785074694791UL;
    private const ulong P2 = 14029467366897019727UL;
    private const ulong P3 = 1609587929392839161UL;
    private const ulong P4 = 9650029242287828579UL;
    private const ulong P5 = 2870177450012600261UL;

    public const int Sha256Size = 32;

    private static ulong Rotl(ulong x, int r) => (x << r) | (x >> (64 - r));

    private static ulong Round(ulong acc, ulong input) => Rotl(acc + (input * P2), 31) * P1;

    private static ulong MergeRound(ulong acc, ulong val) => ((acc ^ Round(0, val)) * P1) + P4;

    private static ulong Read64(ReadOnlySpan<byte> s, int o)
        => s[o] | ((ulong)s[o + 1] << 8) | ((ulong)s[o + 2] << 16) | ((ulong)s[o + 3] << 24)
           | ((ulong)s[o + 4] << 32) | ((ulong)s[o + 5] << 40) | ((ulong)s[o + 6] << 48) | ((ulong)s[o + 7] << 56);

    private static uint Read32(ReadOnlySpan<byte> s, int o)
        => s[o] | ((uint)s[o + 1] << 8) | ((uint)s[o + 2] << 16) | ((uint)s[o + 3] << 24);

    /// <summary>XXH64 with seed 0.</summary>
    public static ulong XxHash64(ReadOnlySpan<byte> data)
    {
        var length = data.Length;
        var p = 0;
        ulong h;

        if (length >= 32)
        {
            var v1 = unchecked(P1 + P2);
            var v2 = P2;
            ulong v3 = 0;
            var v4 = unchecked(0UL - P1);

            while (p <= length - 32)
            {
                v1 = Round(v1, Read64(data, p));
                v2 = Round(v2, Read64(data, p + 8));
                v3 = Round(v3, Read64(data, p + 16));
                v4 = Round(v4, Read64(data, p + 24));
                p += 32;
            }

            h = Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18);
            h = MergeRound(h, v1);
            h = MergeRound(h, v2);
            h = MergeRound(h, v3);
            h = MergeRound(h, v4);
        }
        else
        {
            h = P5;
        }

        h += (ulong)length;

        while (p <= length - 8)
        {
            h ^= Round(0, Read64(data, p));
            h = (Rotl(h, 27) * P1) + P4;
            p += 8;
        }

        if (p <= length - 4)
        {
            h ^= Read32(data, p) * P1;
            h = (Rotl(h, 23) * P2) + P3;
            p += 4;
        }

        while (p < length)
        {
            h ^= data[p] * P5;
            h = Rotl(h, 11) * P1;
            p++;
        }

        h ^= h >> 33;
        h *= P2;
        h ^= h >> 29;
        h *= P3;
        h ^= h >> 32;
        return h;
    }

    public static int PopCount(ulong value)
    {
        var count = 0;

        while (value != 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }

    public static byte[] Sha256(ReadOnlySpan<byte> data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data.ToArray());
    }
}