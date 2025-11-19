// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.ExFat.Internal.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.ExFat.Internal.Partition;
/// <summary>
/// Cluster writer
/// </summary>
/// <seealso cref="IClusterReader" />
public interface IClusterWriter : IClusterReader
{
    /// <summary>
    /// Writes the cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="clusterBuffer">The cluster buffer.</param>
    /// <param name="offset"></param>
    /// <param name="length"></param>
    void WriteCluster(Cluster cluster, byte[] clusterBuffer, int offset, int length);

    /// <summary>
    /// Writes the cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="clusterBuffer">The cluster buffer.</param>
    /// <param name="offset"></param>
    /// <param name="length"></param>
    /// <param name="cancellationToken"></param>
    ValueTask WriteClusterAsync(Cluster cluster, byte[] clusterBuffer, int offset, int length, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the next cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="nextCluster">The next cluster.</param>
    /// <returns></returns>
    void SetNextCluster(Cluster cluster, Cluster nextCluster);

    /// <summary>
    /// Allocates a cluster.
    /// </summary>
    /// <param name="previousClusterHint">A hint about the previous cluster, this allows to allocate the next one, if available</param>
    /// <returns></returns>
    Cluster AllocateCluster(Cluster previousClusterHint);

    /// <summary>
    /// Frees the cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    void FreeCluster(Cluster cluster);
}
