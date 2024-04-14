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
using System.IO;
using DiscUtils.Streams;

namespace DiscUtils.Udf;

internal class DescriptorTag : IByteArraySerializable
{
    public ushort DescriptorCrc;
    public ushort DescriptorCrcLength;
    public ushort DescriptorVersion;
    public byte TagChecksum;
    public TagIdentifier TagIdentifier;
    public uint TagLocation;
    public ushort TagSerialNumber;

    public int Size => 16;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        TagIdentifier = (TagIdentifier)EndianUtilities.ToUInt16LittleEndian(buffer);
        DescriptorVersion = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(2));
        TagChecksum = buffer[4];
        TagSerialNumber = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(6));
        DescriptorCrc = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(8));
        DescriptorCrcLength = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(10));
        TagLocation = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(12));

        return 16;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }

    public static bool IsValid(ReadOnlySpan<byte> buffer)
    {
        byte checkSum = 0;

        if (EndianUtilities.ToUInt16LittleEndian(buffer) == 0)
        {
            return false;
        }

        for (var i = 0; i < 4; ++i)
        {
            checkSum += buffer[i];
        }

        for (var i = 5; i < 16; ++i)
        {
            checkSum += buffer[i];
        }

        return checkSum == buffer[4];
    }

    public static bool TryFromStream(Stream stream, out DescriptorTag result)
    {
        Span<byte> next = stackalloc byte[512];
        stream.ReadExactly(next);
        if (!IsValid(next))
        {
            result = null;
            return false;
        }

        var dt = new DescriptorTag();
        dt.ReadFrom(next);

        result = dt;
        return true;
    }
}