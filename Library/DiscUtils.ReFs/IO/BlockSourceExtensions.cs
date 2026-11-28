namespace DiscUtils.ReFs.IO;

internal static class BlockSourceExtensions
{
    internal static long ClusterOffset(ulong cluster, uint clusterSize)
    {
        if (clusterSize == 0 || cluster > (ulong)long.MaxValue / clusterSize)
        {
            throw new ReFsFormatException($"Cluster 0x{cluster:X} with size {clusterSize} has an unaddressable byte offset.");
        }

        return (long)(cluster * clusterSize);
    }

    extension(IBlockSource source)
    {
        /// <summary>Reads exactly <paramref name="destination" />.Length bytes or throws.</summary>
        public void ReadExactly(long offset, Span<byte> destination)
        {
            if (offset < 0 || offset > source.Length || destination.Length > source.Length - offset)
            {
                throw new ReFsFormatException($"Source range {offset}+{destination.Length} exceeds length {source.Length}.");
            }

            var read = source.Read(offset, destination);

            if (read != destination.Length)
            {
                throw new ReFsFormatException(
                    $"Unexpected end of source: wanted {destination.Length} bytes at {offset}, got {read}.");
            }
        }

        /// <summary>Allocates and reads exactly <paramref name="length" /> bytes.</summary>
        public ReadOnlySpan<byte> ReadBytes(long offset, int length)
        {
            var buffer = new byte[length];
            source.ReadExactly(offset, buffer);
            return buffer;
        }
    }
}