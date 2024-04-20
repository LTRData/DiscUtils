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

/// <summary>
/// Maps logical address to physical
/// </summary>
internal class Stripe : IByteArraySerializable
{
    public static readonly int Length = 0x20;

    /// <summary>
    /// device id
    /// </summary>
    public ulong DeviceId { get; private set; }

    /// <summary>
    /// offset
    /// </summary>
    public ulong Offset { get; private set; }

    /// <summary>
    /// device UUID
    /// </summary>
    public Guid DeviceUuid { get; private set; }

    public int Size => Length;

    public Key DevItemKey => new(DeviceId, ItemType.DevItem, Offset);

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        DeviceId = EndianUtilities.ToUInt64LittleEndian(buffer);
        Offset = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x8));
        DeviceUuid = EndianUtilities.ToGuidLittleEndian(buffer.Slice(0x10));
        return Size;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}
