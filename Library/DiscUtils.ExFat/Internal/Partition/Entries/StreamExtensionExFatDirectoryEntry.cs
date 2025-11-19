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
/// Stream secondary entry
/// </summary>
/// <seealso cref="ExFatDirectoryEntry" />
/// <seealso cref="IDataProvider" />
/// <remarks>
/// Initializes a new instance of the <see cref="StreamExtensionExFatDirectoryEntry"/> class.
/// </remarks>
/// <param name="buffer">The buffer.</param>
[DebuggerDisplay("Stream extension length={ValidDataLength.Value} @{FirstCluster.Value} ({DataLength.Value})")]
public class StreamExtensionExFatDirectoryEntry(Memory<byte> buffer) : ExFatDirectoryEntry(buffer), IDataProvider
{
    /// <summary>
    /// Gets or sets the general secondary flags.
    /// </summary>
    /// <value>
    /// The general secondary flags.
    /// </value>
    public IValueProvider<ExFatGeneralSecondaryFlags> GeneralSecondaryFlags { get; } = new EnumValueProvider<ExFatGeneralSecondaryFlags, byte>(new BufferUInt8(buffer.Slice(1)));
    /// <summary>
    /// Gets or sets the length of the name.
    /// </summary>
    /// <value>
    /// The length of the name.
    /// </value>
    public IValueProvider<byte> NameLength { get; } = new BufferUInt8(buffer.Slice(3));
    /// <summary>
    /// Gets or sets the name hash.
    /// </summary>
    /// <value>
    /// The name hash.
    /// </value>
    public IValueProvider<ushort> NameHash { get; } = new BufferUInt16(buffer.Slice(4));
    /// <summary>
    /// Gets the length of the valid data.
    /// </summary>
    /// <value>
    /// The length of the valid data.
    /// This is lower than or equal to <see cref="DataLength"/>
    /// </value>
    public IValueProvider<ulong> ValidDataLength { get; } = new BufferUInt64(buffer.Slice(8));

    /// <summary>
    /// Gets or sets the first cluster
    /// </summary>
    /// <value>
    /// The first cluster.
    /// </value>
    public IValueProvider<uint> FirstCluster { get; } = new BufferUInt32(buffer.Slice(20));

    /// <summary>
    /// Gets or sets the length of the data.
    /// This is the allocated data length.
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
    public DataDescriptor? DataDescriptor
    {
        get => new(FirstCluster.Value, GeneralSecondaryFlags.Value.HasAny(ExFatGeneralSecondaryFlags.NoFatChain), DataLength.Value, ValidDataLength.Value);
        set
        {
            FirstCluster.Value = value.Value.FirstCluster.ToUInt32();
            if (value.Value.Contiguous)
            {
                GeneralSecondaryFlags.Value |= ExFatGeneralSecondaryFlags.NoFatChain;
            }
            else
            {
                GeneralSecondaryFlags.Value &= ~ExFatGeneralSecondaryFlags.NoFatChain;
            }

            DataLength.Value = value.Value.PhysicalLength;
            ValidDataLength.Value = value.Value.LogicalLength;
        }
    }
}