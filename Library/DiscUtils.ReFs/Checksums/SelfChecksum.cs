using System.Buffers;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Mount;

namespace DiscUtils.ReFs.Checksums;

/// <summary>
///     Self-checksummed blocks (superblock, checkpoint): a record inside the block (a page reference, <see cref="PageRef" />: 0x30 bytes, or 0x68 on
///     ReFS 3.4 to 3.9 and 0x48 with SHA-256) holds the block's own location tuple, a checksum blob header and the digest. The digest covers the whole
///     block with the record zeroed.
/// </summary>
internal static class SelfChecksum
{
    public const int RecordLength = 0x30;
    private const int KindOffset = 0x22;
    private const int DigestLengthOffset = 0x24;
    private const int DigestOffset = 0x28;
    private const int MaxDigestLength = 32;

    public static bool Verify(ReadOnlySpan<byte> block, uint recordOffset, uint recordLength)
    {
        if (recordLength < DigestOffset || recordOffset + (ulong)recordLength > (ulong)block.Length)
        {
            return false;
        }

        var offset = (int)recordOffset;
        var length = (int)recordLength;

        var record = block.Slice(offset, length);
        var kind = (ChecksumKind)record[KindOffset];
        var digestLength = record.U32(DigestLengthOffset);

        if (kind is not (ChecksumKind.Crc32C or ChecksumKind.Crc64 or ChecksumKind.Sha256 or ChecksumKind.Xxh64)
            || digestLength is 0 or > MaxDigestLength
            || DigestOffset + digestLength > recordLength)
        {
            return false;
        }

        var digest = record.Slice(DigestOffset, (int)digestLength);

        var copy = ArrayPool<byte>.Shared.Rent(block.Length);

        try
        {
            var zeroed = copy.AsSpan(0, block.Length);
            block.CopyTo(zeroed);
            zeroed.Slice(offset, length).Clear();
            return PageChecksum.Verify(kind, zeroed, digest);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(copy);
        }
    }
}