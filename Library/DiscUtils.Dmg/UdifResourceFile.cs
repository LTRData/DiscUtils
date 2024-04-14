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

internal class UdifResourceFile : IByteArraySerializable
{
    public UdifChecksum DataForkChecksum;
    public ulong DataForkLength;
    public ulong DataForkOffset;
    public uint Flags;
    public uint HeaderSize;
    public uint ImageVariant;

    public UdifChecksum MasterChecksum;
    public ulong RsrcForkLength;
    public ulong RsrcForkOffset;

    public ulong RunningDataForkOffset;
    public long SectorCount;
    public uint SegmentCount;
    public Guid SegmentGuid;

    public uint SegmentNumber;
    public uint Signature;
    public uint Version;
    public ulong XmlLength;
    public ulong XmlOffset;

    public bool SignatureValid => Signature == 0x6B6F6C79;

    public int Size => 512;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Signature = EndianUtilities.ToUInt32BigEndian(buffer);
        Version = EndianUtilities.ToUInt32BigEndian(buffer.Slice(4));
        HeaderSize = EndianUtilities.ToUInt32BigEndian(buffer.Slice(8));
        Flags = EndianUtilities.ToUInt32BigEndian(buffer.Slice(12));
        RunningDataForkOffset = EndianUtilities.ToUInt64BigEndian(buffer.Slice(16));
        DataForkOffset = EndianUtilities.ToUInt64BigEndian(buffer.Slice(24));
        DataForkLength = EndianUtilities.ToUInt64BigEndian(buffer.Slice(32));
        RsrcForkOffset = EndianUtilities.ToUInt64BigEndian(buffer.Slice(40));
        RsrcForkLength = EndianUtilities.ToUInt64BigEndian(buffer.Slice(48));
        SegmentNumber = EndianUtilities.ToUInt32BigEndian(buffer.Slice(56));
        SegmentCount = EndianUtilities.ToUInt32BigEndian(buffer.Slice(60));
        SegmentGuid = EndianUtilities.ToGuidBigEndian(buffer.Slice(64));

        DataForkChecksum = EndianUtilities.ToStruct<UdifChecksum>(buffer.Slice(80));
        XmlOffset = EndianUtilities.ToUInt64BigEndian(buffer.Slice(216));
        XmlLength = EndianUtilities.ToUInt64BigEndian(buffer.Slice(224));

        MasterChecksum = EndianUtilities.ToStruct<UdifChecksum>(buffer.Slice(352));
        ImageVariant = EndianUtilities.ToUInt32BigEndian(buffer.Slice(488));
        SectorCount = EndianUtilities.ToInt64BigEndian(buffer.Slice(492));

        return Size;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}