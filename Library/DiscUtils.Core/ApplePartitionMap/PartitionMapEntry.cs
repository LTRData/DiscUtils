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
using DiscUtils.Partitions;
using DiscUtils.Streams;

namespace DiscUtils.ApplePartitionMap;

internal sealed class PartitionMapEntry : PartitionInfo, IByteArraySerializable
{
    private readonly Stream _diskStream;
    public uint BootBlock;
    public uint BootBytes;
    public uint Flags;
    public uint LogicalBlocks;
    public uint LogicalBlockStart;
    public uint MapEntries;
    public string Name;
    public uint PhysicalBlocks;
    public uint PhysicalBlockStart;
    public ushort Signature;
    public string Type;

    public PartitionMapEntry(Stream diskStream)
    {
        _diskStream = diskStream;
    }

    public override byte BiosType => 0xAF;

    public override long FirstSector => PhysicalBlockStart;

    public override Guid GuidType => Guid.Empty;

    public override long LastSector => PhysicalBlockStart + PhysicalBlocks - 1;

    public override string TypeAsString => Type;

    public override PhysicalVolumeType VolumeType => PhysicalVolumeType.ApplePartition;

    public int Size => 512;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        Signature = EndianUtilities.ToUInt16BigEndian(buffer);
        MapEntries = EndianUtilities.ToUInt32BigEndian(buffer.Slice(4));
        PhysicalBlockStart = EndianUtilities.ToUInt32BigEndian(buffer.Slice(8));
        PhysicalBlocks = EndianUtilities.ToUInt32BigEndian(buffer.Slice(12));
        Name = latin1Encoding.GetString(buffer.Slice(16, 32)).TrimEnd('\0');
        Type = latin1Encoding.GetString(buffer.Slice(48, 32)).TrimEnd('\0');
        LogicalBlockStart = EndianUtilities.ToUInt32BigEndian(buffer.Slice(80));
        LogicalBlocks = EndianUtilities.ToUInt32BigEndian(buffer.Slice(84));
        Flags = EndianUtilities.ToUInt32BigEndian(buffer.Slice(88));
        BootBlock = EndianUtilities.ToUInt32BigEndian(buffer.Slice(92));
        BootBytes = EndianUtilities.ToUInt32BigEndian(buffer.Slice(96));

        return 512;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }

    public override SparseStream Open()
    {
        return new SubStream(_diskStream, PhysicalBlockStart * 512, PhysicalBlocks * 512);
    }
}