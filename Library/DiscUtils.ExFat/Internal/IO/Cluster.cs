// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Diagnostics;

namespace DiscUtils.ExFat.Internal.IO;
/// <summary>
/// Represents a cluster value
/// </summary>
[DebuggerDisplay("{" + nameof(Value) + "}")]
public readonly struct Cluster
{
    /// <summary>
    /// Gets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    public long Value { get; }

    /// <summary>
    /// Gets a value indicating whether this instance is free.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance is free; otherwise, <c>false</c>.
    /// </value>
    public bool IsFree => Value == 0;

    /// <summary>
    /// Gets a value indicating whether this instance is a data cluster.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance is data; otherwise, <c>false</c>.
    /// </value>
    public bool IsData => Value >= 2;

    /// <summary>
    /// Gets a value indicating whether this instance is last.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance is last; otherwise, <c>false</c>.
    /// </value>
    public bool IsLast => Value is < 0 and >= MinLast;

    /// <summary>
    /// The first data cluster
    /// </summary>
    public static Cluster First { get; } = new(2);
    /// <summary>
    /// Free cluster instance
    /// </summary>
    public static Cluster Free { get; } = new(0);
    /// <summary>
    /// Last cluster of chain
    /// </summary>
    public static Cluster Last { get; } = new(0xFFFFFFFF);
    /// <summary>
    /// Cluster marked bad
    /// </summary>
    public static Cluster Bad { get; } = new(0xFFFFFFF7);
    /// <summary>
    /// The marker
    /// </summary>
    public static Cluster Marker { get; } = new(0xFFFFFFF8);

    private const long MinLast = -8;
    private const uint Reserved32 = 0xFFFFFFF0;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cluster"/> struct.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    public Cluster(uint cluster)
    {
        if (cluster >= Reserved32)
        {
            Value = (int)cluster;
        }
        else
        {
            Value = cluster;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Cluster"/> struct.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    public Cluster(long cluster)
    {
        Value = cluster;
    }

    /// <summary>
    /// Converts value to <see cref="uint"/>.
    /// </summary>
    /// <returns></returns>
    public uint ToUInt32() => (uint)Value;

    /// <summary>
    /// Performs an implicit conversion from <see cref="uint"/> to <see cref="Cluster"/>.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <returns>
    /// The result of the conversion.
    /// </returns>
    public static implicit operator Cluster(uint cluster) => new(cluster);

    /// <summary>
    /// Adds an offset to a cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <returns>
    /// The result of the operator.
    /// </returns>
    public static Cluster operator ++(Cluster cluster) => cluster + 1L;

    /// <summary>
    /// Adds an offset to a cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>
    /// The result of the operator.
    /// </returns>
    public static Cluster operator +(Cluster cluster, int offset) => cluster + (long)offset;

    /// <summary>
    /// Adds an offset to a cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>
    /// The result of the operator.
    /// </returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Cluster operator +(Cluster cluster, long offset)
    {
        if (!cluster.IsData)
        {
            throw new InvalidOperationException();
        }

        return new Cluster(cluster.Value + offset);
    }

    /// <summary>
    /// Subtracts an offset to a cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>
    /// The result of the operator.
    /// </returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Cluster operator -(Cluster cluster, long offset)
    {
        if (!cluster.IsData)
        {
            throw new InvalidOperationException();
        }

        return new Cluster(cluster.Value - offset);
    }

    /// <summary>
    /// Subtracts an offset to a cluster.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <returns>
    /// The result of the operator.
    /// </returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Cluster operator --(Cluster cluster)
    {
        if (!cluster.IsData)
        {
            throw new InvalidOperationException();
        }

        return new Cluster(cluster.Value - 1);
    }

    /// <summary>
    /// Indicates whether two clusters have same value
    /// </summary>
    /// <param name="a">a.</param>
    /// <param name="b">The b.</param>
    /// <returns>
    /// The result of the operator.
    /// </returns>
    public static bool operator ==(Cluster a, Cluster b) => a.Value == b.Value;

    /// <summary>
    /// Indicates whether two clusters have different value
    /// </summary>
    /// <param name="a">a.</param>
    /// <param name="b">The b.</param>
    /// <returns>
    /// The result of the operator.
    /// </returns>
    public static bool operator !=(Cluster a, Cluster b) => !(a == b);

    /// <summary>
    /// Determines whether the specified <see cref="object" />, is equal to this instance.
    /// </summary>
    /// <param name="obj">The <see cref="object" /> to compare with this instance.</param>
    /// <returns>
    ///   <c>true</c> if the specified <see cref="object" /> is equal to this instance; otherwise, <c>false</c>.
    /// </returns>
    public override bool Equals(object obj)
    {
        if (obj is Cluster cluster)
        {
            return Value == cluster.Value;
        }

        return false;
    }

    /// <summary>
    /// Returns a hash code for this instance.
    /// </summary>
    /// <returns>
    /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.
    /// </returns>
    public override int GetHashCode() => (int)Value;
}
