using DiscUtils.ReFs.IO;

namespace DiscUtils.ReFs.Compression;

/// <summary>
///     Everything needed to open compressed containers: the volume bytes, the decompressor, and the compression table
///     (read per container, see <see cref="CompressionTableReader" />).
/// </summary>
internal sealed class CompressionContext(IBlockSource volume, uint clusterSize, IChunkDecompressor decompressor, bool verifyChecksums, CompressionTableReader tables)
{
    public IBlockSource Volume { get; } = volume;

    public uint ClusterSize { get; } = clusterSize;

    public IChunkDecompressor Decompressor { get; } = decompressor;

    public bool VerifyChecksums { get; } = verifyChecksums;

    public IReadOnlyList<CompressionHeader> GetHeaders(ulong containerId) => tables.GetHeaders(containerId);

    public byte[] GetBitmap(ulong containerId) => tables.GetBitmap(containerId);
}