// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Diagnostics;

namespace DiscUtils.ExFat.Internal.IO;
/// <summary>
/// Information about data in partition
/// </summary>
[DebuggerDisplay("@{FirstCluster.Value} ({LogicalLength} / {PhysicalLength}) contiguous={Contiguous}")]
public readonly struct DataDescriptor
{
    /// <summary>
    /// Gets the first cluster of the data in partition.
    /// </summary>
    /// <value>
    /// The first cluster.
    /// </value>
    public Cluster FirstCluster { get; }

    /// <summary>
    /// Gets a value indicating whether this <see cref="DataDescriptor"/> is contiguous.
    /// When data is contiguous there is no need to read the FAT information
    /// </summary>
    /// <value>
    ///   <c>true</c> if contiguous; otherwise, <c>false</c>.
    /// </value>
    public bool Contiguous { get; }

    /// <summary>
    /// The physical length is the allocated data length
    /// </summary>
    /// <value>
    /// The length.
    /// </value>
    public ulong PhysicalLength { get; }

    /// <summary>
    /// The logical length is between 0 and <see cref="PhysicalLength"/>.
    /// </summary>
    /// <value>
    /// The length of the logical.
    /// </value>
    public ulong LogicalLength { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DataDescriptor"/> class.
    /// </summary>
    /// <param name="firstCluster">The first cluster.</param>
    /// <param name="contiguous">if set to <c>true</c> [contiguous].</param>
    /// <param name="physicalLength">The length.</param>
    /// <param name="logicalLength"></param>
    /// <exception cref="ArgumentException">length must be provided for contiguous streams</exception>
    public DataDescriptor(Cluster firstCluster, bool contiguous, ulong physicalLength, ulong logicalLength)
    {
        FirstCluster = firstCluster;
        Contiguous = contiguous;
        PhysicalLength = physicalLength;
        LogicalLength = logicalLength;
    }

    /// <summary>
    /// Determines whether the specified <see cref="object" />, is equal to this instance.
    /// </summary>
    /// <param name="obj">The <see cref="object" /> to compare with this instance.</param>
    /// <returns>
    ///   <c>true</c> if the specified <see cref="object" /> is equal to this instance; otherwise, <c>false</c>.
    /// </returns>
    public override bool Equals(object obj)
    {
        if (obj is not DataDescriptor other)
        {
            return false;
        }

        return FirstCluster == other.FirstCluster && Contiguous == other.Contiguous
            && PhysicalLength == other.PhysicalLength && LogicalLength == other.LogicalLength;
    }

    /// <summary>
    /// Returns a hash code for this instance.
    /// </summary>
    /// <returns>
    /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
    /// </returns>
    public override int GetHashCode() => FirstCluster.Value.GetHashCode() ^ Contiguous.GetHashCode() ^ PhysicalLength.GetHashCode() ^ LogicalLength.GetHashCode();
}