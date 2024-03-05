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

namespace DiscUtils.Wim;

internal class FileHeader : IByteArraySerializable
{
    public uint BootIndex;
    public ShortResourceHeader BootMetaData;
    public int CompressionSize;
    public FileFlags Flags;
    public uint HeaderSize;
    public uint ImageCount;
    public ShortResourceHeader IntegrityHeader;
    public ShortResourceHeader OffsetTableHeader;
    public ushort PartNumber;
    public string Tag;
    public ushort TotalParts;
    public uint Version;
    public Guid WimGuid;
    public ShortResourceHeader XmlDataHeader;

    public int Size => 512;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        Tag = latin1Encoding.GetString(buffer.Slice(0, 8));
        HeaderSize = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(8));
        Version = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(12));
        Flags = (FileFlags)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(16));
        CompressionSize = EndianUtilities.ToInt32LittleEndian(buffer.Slice(20));
        WimGuid = EndianUtilities.ToGuidLittleEndian(buffer.Slice(24));
        PartNumber = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(40));
        TotalParts = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(42));
        ImageCount = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(44));

        OffsetTableHeader = new ShortResourceHeader();
        OffsetTableHeader.Read(buffer.Slice(48));

        XmlDataHeader = new ShortResourceHeader();
        XmlDataHeader.Read(buffer.Slice(72));

        BootMetaData = new ShortResourceHeader();
        BootMetaData.Read(buffer.Slice(96));

        BootIndex = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(120));

        IntegrityHeader = new ShortResourceHeader();
        IntegrityHeader.Read(buffer.Slice(124));

        return Size;
    }

    public bool IsValid()
    {
        return Tag == "MSWIM\0\0\0" && HeaderSize >= 148;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer) => throw new NotImplementedException();
}