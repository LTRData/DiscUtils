namespace DiscUtils.ReFs.Compression;

/// <summary>Rows loaded from the compression table (table id 0xE) by a full scan, keyed by container id.</summary>
internal sealed class CompressionTables
{
    /// <summary>Compression headers (row type 7).</summary>
    public Dictionary<ulong, List<CompressionHeader>> Headers { get; } = [];

    /// <summary>
    ///     Per-container bitmap of the clusters that hold data (row type 3: 0x30-byte header with u32 cluster count @0x20 and
    ///     u32 data offset @0x28, then one bit per cluster). The compressed stream holds exactly these clusters, packed.
    /// </summary>
    public Dictionary<ulong, byte[]> Bitmaps { get; } = [];
}