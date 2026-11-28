namespace DiscUtils.ReFs.Diagnostics.Enums;

/// <summary>A checksum algorithm used by ReFS metadata pages or integrity-stream data.</summary>
public enum ReFsChecksumKind
{
    /// <summary>No checksum, or a kind this reader does not support.</summary>
    None = 0,

    /// <summary>CRC-32C (Castagnoli).</summary>
    Crc32C = 1,

    /// <summary>CRC-64 (the NVMe polynomial).</summary>
    Crc64 = 2,

    /// <summary>SHA-256.</summary>
    Sha256 = 4,

    /// <summary>XXH64 with seed 0.</summary>
    Xxh64 = 5
}