// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DiscUtils.ExFat.Internal.Buffers;
/// <summary>
/// 16-bit char string buffer
/// </summary>
/// <seealso cref="string" />
[DebuggerDisplay("{" + nameof(Value) + "}")]
public readonly struct BufferWideString : IValueProvider<string>
{
    private readonly Memory<byte> buffer;

    private IEnumerable<char> GetChars()
    {
        var all = buffer;
        for (var index = 0; index < all.Length; index += 2)
        {
            yield return ToChar(all.Span[index], all.Span[index + 1]);
        }
    }

    private IEnumerable<char> GetZeroChars()
    {
        foreach (var b in GetChars())
        {
            if (b == 0)
            {
                break;
            }

            yield return b;
        }
    }

    private static char ToChar(byte first, byte second) => (char)(first | second << 8);

    private static byte[] ToBytes(char c) => new[] { (byte)(c & 0xFF), (byte)(c >> 8) };

    /// <inheritdoc />
    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    public string Value
    {
        get => new(GetZeroChars().ToArray());
        set
        {
            for (int byteIndex = 0, charIndex = 0; byteIndex < buffer.Length; byteIndex += 2, charIndex++)
            {
                if (charIndex < value.Length)
                {
                    var t = ToBytes(value[charIndex]);
                    buffer.Span[byteIndex] = t[0];
                    buffer.Span[byteIndex + 1] = t[1];
                }
                else
                {
                    buffer.Span[byteIndex] = 0;
                    buffer.Span[byteIndex + 1] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferByteString" /> class.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="charsLength">The length.</param>
    public BufferWideString(Memory<byte> buffer, int charsLength)
    {
        this.buffer = buffer.Slice(0, charsLength * 2);
    }
}