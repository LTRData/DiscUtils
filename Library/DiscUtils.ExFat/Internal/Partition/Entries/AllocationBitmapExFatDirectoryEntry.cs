// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Diagnostics;
using DiscUtils.ExFat.Internal.Buffers;
using DiscUtils.ExFat.Internal.IO;

namespace DiscUtils.ExFat.Internal.Partition.Entries;
/// <summary>
/// Allocation bitmap directory entry
/// </summary>
/// <seealso cref="ExFatDirectoryEntry" />
/// <seealso cref="IDataProvider" />
[DebuggerDisplay("Allocation bitmap @{FirstCluster.Value} ({DataLength.Value})")]
public class AllocationBitmapExFatDirectoryEntry : ExFatDirectoryEntry, IDataProvider
{
    /// <summary>
    /// Gets the bitmap flags.
    /// </summary>
    /// <value>
    /// The bitmap flags.
    /// </value>
    public IValueProvider<AllocationBitmapFlags> BitmapFlags { get; }

    /// <summary>
    /// Gets or sets the first cluster.
    /// </summary>
    /// <value>
    /// The first cluster.
    /// </value>
    public IValueProvider<uint> FirstCluster { get; }
    /// <summary>
    /// Gets the length of the data.
    /// </summary>
    /// <value>
    /// The length of the data.
    /// </value>
    public IValueProvider<ulong> DataLength { get; }

    /// <inheritdoc />
    /// <summary>
    /// Gets the data descriptor.
    /// </summary>
    /// <value>
    /// The data descriptor or null if none found.
    /// </value>
    public DataDescriptor? DataDescriptor => new(FirstCluster.Value, false, DataLength.Value, DataLength.Value);

    /// <inheritdoc />
    /// <summary>
    /// Initializes a new instance of the <see cref="T:ExFat.Partition.Entries.AllocationBitmapExFatDirectoryEntry" /> class.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public AllocationBitmapExFatDirectoryEntry(Memory<byte> buffer) : base(buffer)
    {
        BitmapFlags = new EnumValueProvider<AllocationBitmapFlags, byte>(new BufferUInt8(buffer.Slice(1)));
        FirstCluster = new BufferUInt32(buffer.Slice(20));
        DataLength = new BufferUInt64(buffer.Slice(24));
    }
}