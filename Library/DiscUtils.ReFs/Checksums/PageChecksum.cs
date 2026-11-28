using System.Buffers.Binary;
using DiscUtils.Internal;
using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.Checksums;

/// <summary>Verification of metadata page digests stored in parent pointers.</summary>
internal static class PageChecksum
{
    private const uint MatchAllWord = 0xABBAFFFE;
    private const uint EscapedMatchAllWord = 0xABBAFFFF;
    private const ulong MatchAllQword = 0xABBAFFFFABBAFFFE;
    private const ulong EscapedMatchAllQword = 0xABBAFFFFABBAFFFF;

    /// <summary>Size in bytes of one stored digest of the given kind (0 when unsupported).</summary>
    public static int DigestLength(ChecksumKind kind) =>
        kind switch
        {
            ChecksumKind.Crc32C => sizeof(uint),
            ChecksumKind.Crc64 or ChecksumKind.Xxh64 => sizeof(ulong),
            ChecksumKind.Sha256 => Hashing.Sha256Size,
            _ => 0
        };

    /// <summary>
    ///     Returns true if <paramref name="data" /> matches <paramref name="digest" /> (little-endian, as stored on disk).
    ///     <see cref="ChecksumKind.None" /> always verifies; unknown kinds are rejected.
    /// </summary>
    public static bool Verify(ChecksumKind kind, ReadOnlySpan<byte> data, ReadOnlySpan<byte> digest) =>
        kind switch
        {
            ChecksumKind.None => true,
            ChecksumKind.Crc32C => digest.Length >= sizeof(uint) && Crc32LittleEndian.Compute(Crc32Algorithm.Castagnoli, data) == BinaryPrimitives.ReadUInt32LittleEndian(digest),
            ChecksumKind.Crc64 => digest.Length >= sizeof(ulong) && Crc64LittleEndian.Compute(Crc64Algorithm.Nvme, data) == BinaryPrimitives.ReadUInt64LittleEndian(digest),
            ChecksumKind.Xxh64 => digest.Length >= sizeof(ulong) && Hashing.XxHash64(data) == BinaryPrimitives.ReadUInt64LittleEndian(digest),
            ChecksumKind.Sha256 => digest.Length >= Hashing.Sha256Size && Hashing.Sha256(data).AsSpan().SequenceEqual(digest.Slice(0, Hashing.Sha256Size)),
            _ => throw new NotSupportedException($"Unsupported checksum kind {(byte)kind}.")
        };

    /// <summary>True when <paramref name="digest" /> is the match-all placeholder Windows writes over an unhashed file-data run.</summary>
    internal static bool IsDataPlaceholder(ChecksumKind kind, ReadOnlySpan<byte> digest) =>
        kind switch
        {
            ChecksumKind.Crc32C => digest.U32(0) == MatchAllWord,
            ChecksumKind.Crc64 or ChecksumKind.Xxh64 => digest.U64(0) == MatchAllQword,
            ChecksumKind.Sha256 => IsSha256Marker(digest, MatchAllWord),
            _ => false
        };

    /// <summary>
    ///     Verifies a file-data block against <paramref name="digest" />, applying Windows' match-all escape on computed hashes
    ///     (file-data checksums escape computed wildcard values; metadata checksums do not).
    /// </summary>
    internal static bool VerifyData(ChecksumKind kind, ReadOnlySpan<byte> data, ReadOnlySpan<byte> digest)
    {
        switch (kind)
        {
            case ChecksumKind.Crc32C:
            {
                var crc = Crc32LittleEndian.Compute(Crc32Algorithm.Castagnoli, data);
                return (crc == MatchAllWord ? EscapedMatchAllWord : crc) == digest.U32(0);
            }
            case ChecksumKind.Crc64:
            {
                var crc = Crc64LittleEndian.Compute(Crc64Algorithm.Nvme, data);
                return (crc == MatchAllQword ? EscapedMatchAllQword : crc) == digest.U64(0);
            }
            case ChecksumKind.Xxh64:
            {
                // XXH64 shares CRC-64's 64-bit match-all value; the escape of a computed match-all value is assumed to be CRC-64's too.
                var hash = Hashing.XxHash64(data);
                return (hash == MatchAllQword ? EscapedMatchAllQword : hash) == digest.U64(0);
            }
            case ChecksumKind.Sha256:
            {
                Span<byte> computed = Hashing.Sha256(data);

                if (IsSha256Marker(computed, MatchAllWord))
                {
                    for (var offset = 0; offset < computed.Length; offset += sizeof(uint))
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(computed[offset..], EscapedMatchAllWord);
                    }
                }

                return computed.SequenceEqual(digest);
            }
            default:
                return Verify(kind, data, digest);
        }
    }

    private static bool IsSha256Marker(ReadOnlySpan<byte> digest, uint word)
    {
        for (var offset = 0; offset < Hashing.Sha256Size; offset += sizeof(uint))
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(digest[offset..]) != word)
            {
                return false;
            }
        }

        return true;
    }
}