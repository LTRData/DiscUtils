namespace DiscUtils.ReFs.Checksums;

/// <summary>Checksum algorithm id stored in the parent pointer's checksum blob (byte +2).</summary>
internal enum ChecksumKind : byte
{
    None = 0,

    /// <summary>CRC-32C (Castagnoli), 4-byte little-endian digest.</summary>
    Crc32C = 1,

    /// <summary>CRC-64, reflected polynomial 0x9A6C9329AC4BC9B5, init/xorout all ones, 8-byte little-endian digest.</summary>
    Crc64 = 2,

    /// <summary>
    ///     SHA-256, 32-byte digest (page references of 0x48 bytes). Metadata is witnessed on a 3.14 volume;
    ///     file-data verification is inferred without a native data witness.
    /// </summary>
    Sha256 = 4,

    /// <summary>
    ///     XXH64 with seed 0, 8-byte little-endian digest. Checksum type 5; seen on metadata and file data of Windows build 29683 images.
    /// </summary>
    Xxh64 = 5
}