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

namespace DiscUtils.HfsPlus;

internal sealed class VolumeHeader : IByteArraySerializable
{
    private static readonly ushort[] HfsPlusSignatures = [0x482b, 0x4858];

    public ForkData AllocationFile;
    public VolumeAttributes Attributes;
    public ForkData AttributesFile;
    public DateTime BackupDate;

    public uint BlockSize;
    public ForkData CatalogFile;
    public DateTime CheckedDate;

    public DateTime CreateDate;
    public uint DataClumpSize;
    public ulong EncodingsBitmap;
    public ForkData ExtentsFile;

    public uint FileCount;

    public uint[] FinderInfo;
    public uint FolderCount;
    public uint FreeBlocks;
    public uint JournalInfoBlock;
    public uint LastMountedVersion;
    public DateTime ModifyDate;

    public uint NextAllocation;
    public CatalogNodeId NextCatalogId;
    public uint ResourceClumpSize;

    public ushort Signature;
    public ForkData StartupFile;
    public uint TotalBlocks;
    public ushort Version;

    public uint WriteCount;

    public bool IsValid => Array.IndexOf(HfsPlusSignatures, Signature) >= 0;

    public int Size => 512;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Signature = EndianUtilities.ToUInt16BigEndian(buffer);
        if (!IsValid)
        {
            return Size;
        }

        Version = EndianUtilities.ToUInt16BigEndian(buffer[2..]);
        Attributes = (VolumeAttributes)EndianUtilities.ToUInt32BigEndian(buffer[4..]);
        LastMountedVersion = EndianUtilities.ToUInt32BigEndian(buffer[8..]);
        JournalInfoBlock = EndianUtilities.ToUInt32BigEndian(buffer[12..]);

        CreateDate = HfsPlusUtilities.ReadHFSPlusDate(DateTimeKind.Local, buffer[16..]);
        ModifyDate = HfsPlusUtilities.ReadHFSPlusDate(DateTimeKind.Utc, buffer[20..]);
        BackupDate = HfsPlusUtilities.ReadHFSPlusDate(DateTimeKind.Utc, buffer[24..]);
        CheckedDate = HfsPlusUtilities.ReadHFSPlusDate(DateTimeKind.Utc, buffer[28..]);

        FileCount = EndianUtilities.ToUInt32BigEndian(buffer[32..]);
        FolderCount = EndianUtilities.ToUInt32BigEndian(buffer[36..]);

        BlockSize = EndianUtilities.ToUInt32BigEndian(buffer[40..]);
        TotalBlocks = EndianUtilities.ToUInt32BigEndian(buffer[44..]);
        FreeBlocks = EndianUtilities.ToUInt32BigEndian(buffer[48..]);

        NextAllocation = EndianUtilities.ToUInt32BigEndian(buffer[52..]);
        ResourceClumpSize = EndianUtilities.ToUInt32BigEndian(buffer[56..]);
        DataClumpSize = EndianUtilities.ToUInt32BigEndian(buffer[60..]);
        NextCatalogId = new CatalogNodeId(EndianUtilities.ToUInt32BigEndian(buffer[64..]));

        WriteCount = EndianUtilities.ToUInt32BigEndian(buffer[68..]);
        EncodingsBitmap = EndianUtilities.ToUInt64BigEndian(buffer[72..]);

        FinderInfo = new uint[8];
        for (var i = 0; i < 8; ++i)
        {
            FinderInfo[i] = EndianUtilities.ToUInt32BigEndian(buffer[(80 + i * 4)..]);
        }

        AllocationFile = EndianUtilities.ToStruct<ForkData>(buffer[112..]);
        ExtentsFile = EndianUtilities.ToStruct<ForkData>(buffer[192..]);
        CatalogFile = EndianUtilities.ToStruct<ForkData>(buffer[272..]);
        AttributesFile = EndianUtilities.ToStruct<ForkData>(buffer[352..]);
        StartupFile = EndianUtilities.ToStruct<ForkData>(buffer[432..]);

        return 512;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}