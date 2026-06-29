// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Diagnostics;

namespace DiscUtils.ExFat.Internal.Buffers;
/// <summary>
/// 8-bit unsigned int buffer (in other words, a byte)
/// </summary>
/// <seealso cref="byte" />
[DebuggerDisplay("{" + nameof(Value) + "}")]
public readonly struct BufferUInt8 : IValueProvider<byte>
{
    private readonly Memory<byte> buffer;

    /// <inheritdoc />
    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    public byte Value
    {
        get => buffer.Span[0];
        set => buffer.Span[0] = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferUInt8"/> class.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public BufferUInt8(Memory<byte> buffer)
    {
        this.buffer = buffer[..sizeof(byte)];
    }
}