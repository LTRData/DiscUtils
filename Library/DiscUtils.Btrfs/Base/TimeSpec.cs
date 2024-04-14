//
// Copyright (c) 2017, Bianco Veigel
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using DiscUtils.Streams;

namespace DiscUtils.Btrfs.Base;

internal class TimeSpec : IByteArraySerializable
{
    public static readonly int Length = 0xc;

    /// <summary>
    /// Number of seconds since 1970-01-01T00:00:00Z. 
    /// </summary>
    public long Seconds { get; internal set; }

    /// <summary>
    /// Number of nanoseconds since the beginning of the second. 
    /// </summary>
    public uint Nanoseconds { get; internal set; }

    public DateTimeOffset Value => DateTimeOffset.FromUnixTimeSeconds(Seconds).AddTicks(Nanoseconds / 100);

    public int Size => Length;

    public DateTimeOffset DateTime => DateTimeOffset.FromUnixTimeSeconds(Seconds).AddTicks(Nanoseconds / 100);

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Seconds = EndianUtilities.ToInt64LittleEndian(buffer);
        Nanoseconds = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x8));
        return Size;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}
