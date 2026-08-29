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
using DiscUtils.Ntfs;

namespace LibraryTests.Ntfs;

public class AttributeListRecordTest
{
    private const byte FixedHeaderSize = 0x1A;

    [Fact]
    public void WriteToUsesFixedHeaderOffsetForUnnamedEntry()
    {
        var record = new AttributeListRecord
        {
            Name = string.Empty
        };

        Span<byte> buffer = stackalloc byte[64];
        buffer.Clear();

        record.WriteTo(buffer);

        Assert.Equal(FixedHeaderSize, record.NameOffset);
        Assert.Equal(FixedHeaderSize, buffer[0x07]);
        Assert.Equal(0x20, record.RecordLength);
        Assert.Equal(record.Size, record.RecordLength);
    }

    [Fact]
    public void WriteToPlacesNameImmediatelyAfterFixedHeader()
    {
        var record = new AttributeListRecord
        {
            Name = "AB"
        };

        Span<byte> buffer = stackalloc byte[64];
        buffer.Clear();

        record.WriteTo(buffer);

        Assert.Equal(FixedHeaderSize, record.NameOffset);
        Assert.Equal(FixedHeaderSize, buffer[0x07]);
        Assert.Equal((byte)'A', buffer[FixedHeaderSize]);
        Assert.Equal((byte)0, buffer[FixedHeaderSize + 1]);
        Assert.Equal((byte)'B', buffer[FixedHeaderSize + 2]);
        Assert.Equal((byte)0, buffer[FixedHeaderSize + 3]);
        Assert.Equal(0x20, record.RecordLength);
        Assert.Equal(record.Size, record.RecordLength);
    }
}
