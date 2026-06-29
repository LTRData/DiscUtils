// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.Streams;
using System;
using System.Diagnostics;

namespace DiscUtils.ExFat.Internal.Buffers;
/// <summary>
/// 32-bits unsigned int buffer
/// </summary>
/// <seealso cref="uint" />
[DebuggerDisplay("{" + nameof(Value) + "}")]
public readonly struct BufferUInt32 : IValueProvider<uint>
{
    private readonly Memory<byte> _buffer;

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    public uint Value
    {
        get => EndianUtilities.ToUInt32LittleEndian(_buffer.Span);
        set => EndianUtilities.WriteBytesLittleEndian(value, _buffer.Span);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferUInt32"/> class.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public BufferUInt32(Memory<byte> buffer)
    {
        _buffer = buffer[..sizeof(uint)];
    }
}