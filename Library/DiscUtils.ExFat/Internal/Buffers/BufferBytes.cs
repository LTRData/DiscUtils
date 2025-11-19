// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace DiscUtils.ExFat.Internal.Buffers;
/// <summary>
/// Represents bytes in the buffer
/// </summary>
[DebuggerDisplay("{" + nameof(DebugLiteral) + "}")]
public readonly struct BufferBytes : IEnumerable<byte>
{
    private readonly Memory<byte> buffer;

    /// <summary>
    /// Gets or sets the <see cref="byte"/> at the specified index.
    /// </summary>
    /// <value>
    /// The <see cref="byte"/>.
    /// </value>
    /// <param name="index">The index.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// </exception>
    public byte this[int index]
    {
        get => buffer.Span[index];
        set => buffer.Span[index] = value;
    }

    private string DebugLiteral
    {
        get
        {
            var s = string.Join(", ", this.Take(10).Select(b => $"0x{b:X2}"));
            if (buffer.Length > 10)
            {
                s += " ...";
            }

            return s;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferBytes"/> class.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public BufferBytes(Memory<byte> buffer)
    {
        this.buffer = buffer;
    }

    /// <summary>
    /// Sets the specified bytes.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    public void Set(ReadOnlySpan<byte> bytes)
    {
        for (var offset = 0; offset < buffer.Length; offset++)
        {
            buffer.Span[offset] = bytes[offset];
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    /// <returns>
    /// A <see cref="T:System.Collections.Generic.IEnumerator`1" /> that can be used to iterate through the collection.
    /// </returns>
    public IEnumerator<byte> GetEnumerator() => MemoryMarshal.ToEnumerable<byte>(buffer).GetEnumerator();

    /// <summary>
    /// Returns an enumerator that iterates through a collection.
    /// </summary>
    /// <returns>
    /// An <see cref="T:System.Collections.IEnumerator" /> object that can be used to iterate through the collection.
    /// </returns>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}