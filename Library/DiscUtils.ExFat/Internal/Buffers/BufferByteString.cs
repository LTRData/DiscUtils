// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DiscUtils.ExFat.Internal.Buffers;
/// <summary>
/// 8-bit char string
/// </summary>
/// <seealso cref="string" />
[DebuggerDisplay("{" + nameof(Value) + "}")]
public readonly struct BufferByteString : IValueProvider<string>
{
    private readonly Encoding encoding;
    private readonly Memory<byte> buffer;

    private IEnumerable<byte> GetZeroBytes()
    {
        foreach (var b in MemoryMarshal.ToEnumerable<byte>(buffer))
        {
            if (b == 0)
            {
                break;
            }

            yield return b;
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    public string Value
    {
        get => encoding.GetString(GetZeroBytes().ToArray());
        set
        {
            var stringBytes = encoding.GetBytes(value);
            // first of all, inject bytes
            buffer.Set(stringBytes);
            // then pad
            for (var index = stringBytes.Length; index < buffer.Length; index++)
            {
                buffer.Span[index] = 0;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferByteString" /> class.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="encoding">The encoding (defaults to ASCII).</param>
    public BufferByteString(Memory<byte> buffer, Encoding encoding = null)
    {
        this.buffer = buffer;
        this.encoding = encoding ?? Encoding.ASCII;
    }
}