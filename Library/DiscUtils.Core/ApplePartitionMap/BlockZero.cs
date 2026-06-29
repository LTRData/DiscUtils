//
// Copyright (c) 2008-2011, Kenneth Bell
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

namespace DiscUtils.ApplePartitionMap;

internal struct BlockZero : IByteArraySerializable
{
    public uint BlockCount { get; private set; }
    public ushort BlockSize { get; private set; }
    public ushort DeviceId { get; private set; }
    public ushort DeviceType { get; private set; }
    public ushort DriverCount { get; private set; }
    public uint DriverData { get; private set; }
    public ushort Signature { get; private set; }

    public int Size => 512;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Signature = EndianUtilities.ToUInt16BigEndian(buffer);
        BlockSize = EndianUtilities.ToUInt16BigEndian(buffer[2..]);
        BlockCount = EndianUtilities.ToUInt32BigEndian(buffer[4..]);
        DeviceType = EndianUtilities.ToUInt16BigEndian(buffer[8..]);
        DeviceId = EndianUtilities.ToUInt16BigEndian(buffer[10..]);
        DriverData = EndianUtilities.ToUInt32BigEndian(buffer[12..]);
        DriverCount = EndianUtilities.ToUInt16LittleEndian(buffer[16..]);

        return 512;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}