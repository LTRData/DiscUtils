namespace DiscUtils.Internal;

internal enum Crc64Algorithm
{
    /// <summary>
    /// Used in NVMe, ReFS and Windows Data Deduplication (aka CRC-64/NVME). Polynomial 0xAD93D23594C93659.
    /// </summary>
    Nvme = 0,

    /// <summary>
    /// Used in XZ (reflected) and ECMA-182 (aka CRC-64/XZ, CRC-64/WE). Polynomial 0x42F0E1EBA9EA3693.
    /// </summary>
    Ecma182 = 1,

    /// <summary>
    /// ISO 3309 (aka CRC-64/GO-ISO). Polynomial 0x000000000000001B.
    /// </summary>
    Iso = 2
}
