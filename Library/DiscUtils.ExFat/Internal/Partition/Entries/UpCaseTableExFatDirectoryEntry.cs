// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Diagnostics;
using DiscUtils.ExFat.Internal.Buffers;
using DiscUtils.ExFat.Internal.IO;

namespace DiscUtils.ExFat.Internal.Partition.Entries;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable IDE0057 // Use range operator

/// <summary>
/// Directory entry for up-case table
/// </summary>
/// <seealso cref="T:ExFat.Partition.Entries.ExFatDirectoryEntry" />
/// <seealso cref="T:ExFat.IO.IDataProvider" />
/// <inheritdoc />
/// <remarks>
/// Initializes a new instance of the <see cref="T:ExFat.Partition.Entries.UpCaseTableExFatDirectoryEntry" /> class.
/// </remarks>
/// <param name="buffer">The buffer.</param>
[DebuggerDisplay("Up case table @{FirstCluster.Value} ({DataLength.Value})")]
public class UpCaseTableExFatDirectoryEntry(Memory<byte> buffer) : ExFatDirectoryEntry(buffer), IDataProvider
{
    /// <summary>
    /// Gets or sets the table checksum.
    /// </summary>
    /// <value>
    /// The table checksum.
    /// </value>
    public IValueProvider<uint> TableChecksum { get; } = new BufferUInt32(buffer.Slice(4));

    /// <summary>
    /// Gets or sets the first cluster.
    /// </summary>
    /// <value>
    /// The first cluster.
    /// </value>
    public IValueProvider<uint> FirstCluster { get; } = new BufferUInt32(buffer.Slice(20));

    /// <summary>
    /// Gets or sets the length of the data.
    /// </summary>
    /// <value>
    /// The length of the data.
    /// </value>
    public IValueProvider<ulong> DataLength { get; } = new BufferUInt64(buffer.Slice(24));

    /// <inheritdoc />
    /// <summary>
    /// Gets the data descriptor.
    /// </summary>
    /// <value>
    /// The data descriptor or null if none found.
    /// </value>
    public DataDescriptor? DataDescriptor => new(FirstCluster.Value, false, DataLength.Value, DataLength.Value);
}