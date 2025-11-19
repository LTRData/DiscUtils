// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.ExFat.Internal.IO;

namespace DiscUtils.ExFat.Internal.Partition;
/// <summary>
/// Allows to access cluter information and data
/// </summary>
public interface IClusterReader
{
    /// <summary>
    /// Gets the cluster size, in bytes
    /// </summary>
    /// <value>
    /// The bytes per cluster.
    /// </value>
    int BytesPerCluster { get; }

    /// <summary>
    /// Reads one cluster.
    /// </summary>
    /// <param name="cluster">The cluster number.</param>
    /// <param name="clusterBuffer">The cluster buffer. It must be large enough to contain full cluster</param>
    /// <param name="offset"></param>
    /// <param name="length"></param>
    void ReadCluster(Cluster cluster, byte[] clusterBuffer, int offset, int length);

    /// <summary>
    /// Reads one cluster.
    /// </summary>
    /// <param name="cluster">The cluster number.</param>
    /// <param name="clusterBuffer">The cluster buffer. It must be large enough to contain full cluster</param>
    /// <param name="offset"></param>
    /// <param name="length"></param>
    /// <param name="cancellationToken"></param>
    ValueTask ReadClusterAsync(Cluster cluster, byte[] clusterBuffer, int offset, int length, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the next item for a given cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <returns></returns>
    Cluster GetNextCluster(Cluster cluster);

    /// <summary>
    /// Gets the clusters described by the <see cref="DataDescriptor"/>.
    /// </summary>
    /// <param name="dataDescriptor">The data descriptor.</param>
    /// <returns></returns>
    IEnumerable<Cluster> GetClusters(DataDescriptor dataDescriptor);
}
