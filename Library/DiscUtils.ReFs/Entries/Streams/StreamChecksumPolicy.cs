using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Entries.Files;

namespace DiscUtils.ReFs.Entries.Streams;

/// <summary>
///     Volume-wide rule for the per-block checksums of integrity-stream data, as Windows derives it:
///     <list type="bullet">
///         <item>
///             this reader supports CRC-64, SHA-256 and XXH64 metadata; SHA-256 file-data semantics are inferred without independent confirmation. XXH64 file
///             data (XXH64 with seed 0 over each block) is confirmed on a Windows build 29683 image; earlier Windows builds accept only types 1, 2 and 4;
///         </item>
///         <item>with CRC-64 metadata, streams use CRC32C when the cluster size is 4 KiB, otherwise CRC-64; other types are used as they are;</item>
///         <item>the checksum block is 4 KiB when clusters are 4 KiB, otherwise 16 KiB.</item>
///     </list>
/// </summary>
internal readonly record struct StreamChecksumPolicy(ChecksumKind Kind, int BlockSize)
{
    private const uint SmallClusterSize = 4096;
    private const int SmallBlockSize = 4096;
    private const int LargeBlockSize = 16384;

    public int DigestLength => PageChecksum.DigestLength(Kind);

    /// <summary>Returns the policy, or null when the volume uses a checksum configuration this reader does not support.</summary>
    public static StreamChecksumPolicy? Derive(ChecksumKind metadataKind, uint clusterSize)
    {
        // For CRC-64 metadata the stream type depends on the cluster size, otherwise it equals the metadata type.
        ChecksumKind streamKind = metadataKind switch
        {
            ChecksumKind.Crc64 => clusterSize == SmallClusterSize ? ChecksumKind.Crc32C : ChecksumKind.Crc64,
            ChecksumKind.Sha256 => ChecksumKind.Sha256,
            ChecksumKind.Xxh64 => ChecksumKind.Xxh64,
            _ => ChecksumKind.None
        };

        return streamKind == ChecksumKind.None
            ? null
            : new StreamChecksumPolicy(streamKind, clusterSize == SmallClusterSize ? SmallBlockSize : LargeBlockSize);
    }
}