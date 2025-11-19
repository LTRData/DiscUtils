// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.IO;
using DiscUtils.ExFat.Internal.IO;

namespace DiscUtils.ExFat.Internal.Partition;
/// <summary>
/// Allocation bitmap manager. Allows to allocate or free clusters from bitmap.
/// This is where you start when you want a free cluster, then it needs to be chained... If not contiguous
/// </summary>
public class ExFatAllocationBitmap
{
    private byte[] _bitmap;
    private Stream _dataStream;
    private uint _firstCluster;
    private bool _delayWrite;
    private readonly object _lock = new();

    /// <summary>
    /// Gets the length.
    /// </summary>
    /// <value>
    /// The length.
    /// </value>
    public long Length { get; private set; }

    /// <summary>
    /// Opens the specified data stream.
    /// </summary>
    /// <param name="dataStream">The data stream.</param>
    /// <param name="firstCluster">The first cluster.</param>
    /// <param name="totalClusters">The total clusters.</param>
    /// <param name="delayWrite">if set to <c>true</c> [delay write].</param>
    public void Open(Stream dataStream, uint firstCluster, long totalClusters, bool delayWrite)
    {
        _dataStream = dataStream;
        _firstCluster = firstCluster;
        _bitmap = new byte[(totalClusters + 7) / 8];
        _delayWrite = delayWrite;
        dataStream?.Read(_bitmap, 0, _bitmap.Length);
        Length = totalClusters;
    }

    /// <summary>
    /// Writes the specified data stream.
    /// </summary>
    /// <param name="dataStream">The data stream.</param>
    public void Write(Stream dataStream)
    {
        lock (_lock)
        {
            _dataStream = dataStream;
            dataStream.Write(_bitmap, 0, _bitmap.Length);
        }
    }

    /// <summary>
    /// Flushes this instance.
    /// </summary>
    public void Flush()
    {
        // only on delay write, because otherwise the bitmap is always up to date
        if (_delayWrite)
        {
            _dataStream.Seek(0, SeekOrigin.Begin);
            _dataStream.Write(_bitmap, 0, _bitmap.Length);
        }

        _dataStream.Flush();
    }

    /// <summary>
    /// Releases resources.
    /// </summary>
    public void Dispose()
    {
        Flush();
        _dataStream.Dispose();
    }

    /// <summary>
    /// Indicates whether the specified cluster is free
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException">cluster</exception>
    public bool GetAt(Cluster cluster)
    {
        lock (_lock)
        {
            if (cluster.Value < _firstCluster || cluster.Value >= Length)
            {
                throw new ArgumentOutOfRangeException(nameof(cluster));
            }

            var clusterIndex = cluster.Value - _firstCluster;
            return GetAtIndex(clusterIndex);
        }
    }

    private bool GetAtIndex(long clusterIndex)
    {
        lock (_lock)
        {
            var byteIndex = (int)clusterIndex / 8;
            var bitMask = 1 << (int)(clusterIndex & 7);
            return (_bitmap[byteIndex] & bitMask) != 0;
        }
    }

    /// <summary>
    /// Sets the allocation for the given cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="allocated">if set to <c>true</c> [allocated].</param>
    /// <returns></returns>
    private int SetAllocation(Cluster cluster, bool allocated)
    {
        var clusterIndex = cluster.Value - _firstCluster;
        var byteIndex = (int)clusterIndex / 8;
        var bitMask = 1 << (int)(clusterIndex & 7);
        if (allocated)
        {
            _bitmap[byteIndex] |= (byte)bitMask;
        }
        else
        {
            _bitmap[byteIndex] &= (byte)~bitMask;
        }

        return byteIndex;
    }

    /// <summary>
    /// Writes the specified byte(s) to disk.
    /// </summary>
    /// <param name="byteIndex">Index of the byte.</param>
    /// <param name="length">The length.</param>
    private void Write(int byteIndex, int length)
    {
        if (_dataStream != null && !_delayWrite)
        {
            _dataStream.Seek(byteIndex, SeekOrigin.Begin);
            _dataStream.Write(_bitmap, byteIndex, length);
        }
    }

    /// <summary>
    /// Gets the used clusters.
    /// This is an optimistic version (since the result may change all the time)
    /// </summary>
    /// <returns></returns>
    public long GetUsedClusters()
    {
        long usedClusters = 0;
        for (var clusterIndex = 0; clusterIndex < Length - _firstCluster;)
        {
            if (clusterIndex % 8 == 0)
            {
                if (_bitmap[clusterIndex / 8] == 0xFF)
                {
                    usedClusters += 8;
                }

                clusterIndex += 8;
            }
            else
            {
                if (GetAtIndex(clusterIndex++))
                {
                    usedClusters++;
                }
            }
        }

        return usedClusters;
    }

    /// <summary>
    /// Allocates a cluster.
    /// </summary>
    /// <param name="contigous">The contigous clusters wanted.</param>
    /// <returns></returns>
    public Cluster Allocate(int contigous = 1)
    {
        lock (_lock)
        {
            return Allocate(FindAvailable(_firstCluster, contigous), contigous) ?? Cluster.Free;
        }
    }

    /// <summary>
    /// Allocates a cluster, at (or after) the specified cluster.
    /// </summary>
    /// <param name="hint">The hint.</param>
    /// <param name="contigous">The contigous clusters wanted.</param>
    /// <returns></returns>
    public Cluster Allocate(Cluster hint, int contigous = 1)
    {
        lock (_lock)
        {
            return Allocate(FindAvailable(hint, contigous) ?? FindAvailable(_firstCluster, contigous), contigous) ?? Cluster.Free;
        }
    }

    private Cluster? Allocate(Cluster? first, int contigous)
    {
        if (!first.HasValue)
        {
            return null;
        }

        int? firstByteIndex = null;
        var lastByteIndex = 0;
        for (var index = 0; index < contigous; index++)
        {
            lastByteIndex = SetAllocation(first.Value + index, true);
            if (!firstByteIndex.HasValue)
            {
                firstByteIndex = lastByteIndex;
            }
        }

        if (firstByteIndex.HasValue)
        {
            Write(firstByteIndex.Value, lastByteIndex - firstByteIndex.Value + 1);
        }

        return first.Value;
    }

    /// <summary>
    /// Finds one or more unallocated cluster.
    /// Does not allocate them, so all allocation process must be perform form within a lock
    /// </summary>
    /// <param name="first">The first.</param>
    /// <param name="contiguous">The contiguous.</param>
    /// <returns></returns>
    private Cluster? FindAvailable(Cluster first, int contiguous = 1)
    {
        if (!first.IsData)
        {
            return null;
        }

        uint freeCluster = 0;
        var unallocatedCount = 0;
        for (var cluster = first.ToUInt32(); cluster < Length;)
        {
            // special case: byte is filled, skip the block (and reset the search)
            if (((cluster - _firstCluster) & 0x07) == 0 && _bitmap[cluster / 8] == 0xFF)
            {
                freeCluster = 0;
                unallocatedCount = 0;
                cluster += 8;
                continue;
            }
            // if it's free, count it
            if (!GetAt(cluster))
            {
                // first to be free, keep it
                if (unallocatedCount == 0)
                {
                    freeCluster = cluster;
                }

                unallocatedCount++;

                // when the amount is reached, return it
                if (unallocatedCount == contiguous)
                {
                    return freeCluster;
                }
            }

            ++cluster;
        }
        // nothing found
        return null;
    }

    /// <summary>
    /// Frees the specified cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    public void Free(Cluster cluster)
    {
        lock (_lock)
        {
            var byteIndex = SetAllocation(cluster, false);
            Write(byteIndex, 1);
        }
    }
}
