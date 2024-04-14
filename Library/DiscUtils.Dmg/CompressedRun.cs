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

namespace DiscUtils.Dmg;

internal class CompressedRun : IByteArraySerializable
{
    public long CompLength;
    public long CompOffset;
    public long SectorCount;
    public long SectorStart;
    public RunType Type;

    public int Size => 40;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Type = (RunType)EndianUtilities.ToUInt32BigEndian(buffer);
        SectorStart = EndianUtilities.ToInt64BigEndian(buffer.Slice(8));
        SectorCount = EndianUtilities.ToInt64BigEndian(buffer.Slice(16));
        CompOffset = EndianUtilities.ToInt64BigEndian(buffer.Slice(24));
        CompLength = EndianUtilities.ToInt64BigEndian(buffer.Slice(32));

        return 40;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}