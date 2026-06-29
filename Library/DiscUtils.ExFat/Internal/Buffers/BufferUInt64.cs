// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.Streams;
using System;
using System.Diagnostics;

namespace DiscUtils.ExFat.Internal.Buffers;
/// <summary>
/// 64-bits unsigned int buffer
/// </summary>
/// <seealso cref="ulong" />
[DebuggerDisplay("{" + nameof(Value) + "}")]
public readonly struct BufferUInt64 : IValueProvider<ulong>
{
    private readonly Memory<byte> buffer;

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    public ulong Value
    {
        get => EndianUtilities.ToUInt64LittleEndian(buffer.Span);
        set => EndianUtilities.WriteBytesLittleEndian(value, buffer.Span);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferUInt64"/> class.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public BufferUInt64(Memory<byte> buffer)
    {
        this.buffer = buffer[..sizeof(ulong)];
    }
}