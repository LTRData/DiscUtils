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
using DiscUtils.Partitions;
using DiscUtils.Streams;

namespace DiscUtils.Lvm.LinuxRaid;
using LogicalVolumeStatus=DiscUtils.LogicalVolumeStatus;
internal class LinuxRaidVolume
{
    private readonly LinuxRaidDiskGroup _group;

    internal LinuxRaidVolume(LinuxRaidDiskGroup group)
    {
        _group = group;
    }

	public Guid Guid => _group.ArrayUuid;

	public string Identity => $"RAID:{Guid:D}";

    public long Length => CalculateVolumeLength();

    public byte BiosType => BiosPartitionTypes.LinuxRaidAutoDetect;

    public LogicalVolumeStatus Status => _group.GetVolumeStatus();

    public SparseStream Open()
    {
        return _group.OpenVolume();
    }

    private long CalculateVolumeLength()
    {
        var memberDiskSize = GetMemberDiskSize();
        
        switch (_group.RaidLevel)
        {
            case 0: // RAID 0 - total of all disks
                return _group.DiskCount * memberDiskSize;

            case 1: // RAID 1 - size of one disk (they're mirrors)
                return memberDiskSize;

            case 5: // RAID 5 - (n-1) * disk_size (one disk for parity)
                return (_group.DiskCount - 1) * memberDiskSize;

            case 6: // RAID 6 - (n-2) * disk_size (two disks for parity)
                return Math.Max(0, _group.DiskCount - 2) * memberDiskSize;

            default:
                // For unknown RAID levels, return the size of the first disk
                return memberDiskSize;
        }
    }

    private long GetMemberDiskSize()
    {
        // Access the first disk through the exposed property
        var firstDisk = _group.FirstDisk;
        if (firstDisk != null)
        {
            // Convert from sectors to bytes
            return (long)firstDisk.ArraySize * Sizes.Sector;
        }
        return 0;
    }
}
