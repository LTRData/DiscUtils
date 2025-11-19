// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.Streams;
using System;
using System.Diagnostics;

namespace DiscUtils.ExFat.Internal.Buffers;
/// <summary>
/// 16-bits unsigned int buffer
/// </summary>
/// <seealso cref="ushort" />
[DebuggerDisplay("{" + nameof(Value) + "}")]
public readonly struct BufferUInt16 : IValueProvider<ushort>
{
    private readonly Memory<byte> buffer;

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    public ushort Value
    {
        get => EndianUtilities.ToUInt16LittleEndian(buffer.Span);
        set => EndianUtilities.WriteBytesLittleEndian(value, buffer.Span);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferUInt16"/> class.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public BufferUInt16(Memory<byte> buffer)
    {
        this.buffer = buffer.Slice(0, sizeof(ushort));
    }
}