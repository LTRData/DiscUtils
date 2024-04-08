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

namespace DiscUtils.Ext;

internal class SuperBlock : IByteArraySerializable
{
    public const ushort Ext2Magic = 0xEF53;

    /// <summary>
    /// Old revision, not supported by DiscUtils.
    /// </summary>
    public const uint OldRevision = 0;

    public ushort BlockGroupNumber;
    public uint BlocksCount;
    public uint BlocksCountHigh;
    public uint BlocksPerGroup;
    public uint CheckInterval;
    public CompatibleFeatures CompatibleFeatures;
    public uint CompressionAlgorithmUsageBitmap;
    public uint CreatorOS;
    public byte DefaultHashVersion;
    public uint DefaultMountOptions;
    public ushort DefaultReservedBlockGid;
    public ushort DefaultReservedBlockUid;
    public ushort DescriptorSize;
    public byte DirPreallocateBlockCount;
    public uint ReservedGDTBlocks;
    public ushort Errors;
    public uint FirstDataBlock;

    public uint FirstInode;
    public uint FirstMetablockBlockGroup;
    public uint Flags;
    public uint FragsPerGroup;
    public uint FreeBlocksCount;
    public uint FreeBlocksCountHigh;
    public uint FreeInodesCount;
    public uint[] HashSeed;
    public IncompatibleFeatures IncompatibleFeatures;

    public uint InodesCount;
    public ushort InodeSize;
    public uint InodesPerGroup;
    public uint[] JournalBackup;
    public uint JournalDevice;
    public uint JournalInode;

    public Guid JournalSuperBlockUniqueId;
    public uint LastCheckTime;
    public string LastMountPoint;
    public uint LastOrphan;
    public uint LogBlockSize;
    public uint LogFragSize;
    public byte LogGroupsPerFlex;
    public uint OverheadBlocksCount;
    public ushort Magic;
    public ushort MaxMountCount;
    public ushort MinimumExtraInodeSize;
    public ushort MinorRevisionLevel;
    public uint MkfsTime;
    public ushort MountCount;
    public uint MountTime;
    public ulong MultiMountProtectionBlock;
    public ushort MultiMountProtectionInterval;

    public byte PreallocateBlockCount;
    public ushort RaidStride;
    public uint RaidStripeWidth;
    public ReadOnlyCompatibleFeatures ReadOnlyCompatibleFeatures;
    public uint ReservedBlocksCount;
    public uint ReservedBlocksCountHigh;
    public uint RevisionLevel;
    public ushort State;
    public Guid UniqueId;
    public string VolumeName;
    public ushort WantExtraInodeSize;
    public uint WriteTime;

    public bool Has64Bit
    {
        get
        {
            return (IncompatibleFeatures & IncompatibleFeatures.SixtyFourBit) ==
                   IncompatibleFeatures.SixtyFourBit && DescriptorSize >= 64;
        }
    }

    public uint BlockSize
    {
        get { return (uint)(1024 << (int)LogBlockSize); }
    }

    public int Size
    {
        get { return 1024; }
    }

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Magic = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(56));
        if (Magic != Ext2Magic)
        {
            return Size;
        }

        InodesCount = EndianUtilities.ToUInt32LittleEndian(buffer);
        BlocksCount = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(4));
        ReservedBlocksCount = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(8));
        FreeBlocksCount = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(12));
        FreeInodesCount = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(16));
        FirstDataBlock = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(20));
        LogBlockSize = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(24));
        LogFragSize = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(28));
        BlocksPerGroup = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(32));
        FragsPerGroup = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(36));
        InodesPerGroup = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(40));
        MountTime = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(44));
        WriteTime = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(48));
        MountCount = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(52));
        MaxMountCount = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(54));
        State = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(58));
        Errors = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(60));
        MinorRevisionLevel = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(62));
        LastCheckTime = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(64));
        CheckInterval = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(68));
        CreatorOS = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(72));
        RevisionLevel = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(76));
        DefaultReservedBlockUid = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(80));
        DefaultReservedBlockGid = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(82));

        FirstInode = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(84));
        InodeSize = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(88));
        BlockGroupNumber = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(90));
        CompatibleFeatures = (CompatibleFeatures)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(92));
        IncompatibleFeatures = (IncompatibleFeatures)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(96));
        ReadOnlyCompatibleFeatures =
            (ReadOnlyCompatibleFeatures)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(100));
        UniqueId = EndianUtilities.ToGuidLittleEndian(buffer.Slice(104));
        VolumeName = EndianUtilities.BytesToZString(buffer.Slice(120, 16));
        LastMountPoint = EndianUtilities.BytesToZString(buffer.Slice(136, 64));
        CompressionAlgorithmUsageBitmap = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(200));

        PreallocateBlockCount = buffer[204];
        DirPreallocateBlockCount = buffer[205];
        ReservedGDTBlocks = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(206));

        JournalSuperBlockUniqueId = EndianUtilities.ToGuidLittleEndian(buffer.Slice(208));
        JournalInode = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(224));
        JournalDevice = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(228));
        LastOrphan = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(232));
        HashSeed = new uint[4];
        HashSeed[0] = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(236));
        HashSeed[1] = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(240));
        HashSeed[2] = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(244));
        HashSeed[3] = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(248));
        DefaultHashVersion = buffer[252];
        DescriptorSize = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(254));
        DefaultMountOptions = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(256));
        FirstMetablockBlockGroup = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(260));
        MkfsTime = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(264));

        JournalBackup = new uint[17];
        for (var i = 0; i < 17; ++i)
        {
            JournalBackup[i] = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(268 + 4 * i));
        }

        BlocksCountHigh = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(336));
        ReservedBlocksCountHigh = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(340));
        FreeBlocksCountHigh = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(344));
        MinimumExtraInodeSize = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(348));
        WantExtraInodeSize = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(350));
        Flags = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(352));
        RaidStride = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(356));
        MultiMountProtectionInterval = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(358));
        MultiMountProtectionBlock = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(360));
        RaidStripeWidth = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(368));
        LogGroupsPerFlex = buffer[372];

        OverheadBlocksCount = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(584));

        return 1024;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}
