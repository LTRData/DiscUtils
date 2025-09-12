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

using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using DiscUtils.Internal;
using DiscUtils.Partitions;

namespace DiscUtils.Lvm.LinuxRaid;

[LogicalVolumeFactory]
internal class LinuxRaidDiskManagerFactory : LogicalVolumeFactory {
	public override bool HandlesPhysicalVolume(PhysicalVolumeInfo volume) {
		var pi = volume.Partition;
		if (pi == null)//i think even if is a full disk it creates a partition that is just the disk in size
			return false;

		if (IsLinuxRaidPartition(pi))
			return true;
		else {
			var sb = LinuxRaidDiskVolume.GetSuperblock(volume.Partition);
			if (sb?.IsValid == true)
				return true;
		}


		return false; // We only handle partitions for now
	}

	public override void MapDisks(IEnumerable<VirtualDisk> disks, Dictionary<string, LogicalVolumeInfo> result) {
		var raidPartitions = new List<LinuxRaidDiskVolume>();

		// Find all Linux RAID partitions across all disks
		foreach (var disk in disks) {
			if (disk.IsPartitioned) {
				foreach (var partition in disk.Partitions.Partitions) {
					if (IsLinuxRaidPartition(partition)) {
						// Create a temporary PhysicalVolumeInfo for this partition to test it

						var partitionStream = partition.Open();

						var superBlock = LinuxRaidDiskVolume.GetSuperblock(partition);
						if (superBlock?.IsValid == true) {
							var physicalVolume = new PhysicalVolumeInfo(superBlock.ArrayUuid.ToString(), disk, partition);
							var raidDisk = new LinuxRaidDiskVolume(physicalVolume, superBlock);

							// Only support RAID 1 for now
							if (raidDisk.RaidLevel == 1)
								raidPartitions.Add(raidDisk);

						}
					}
				}
			}
		}

		// Group RAID partitions by array UUID
		var raidGroups = raidPartitions
			.GroupBy(rp => rp.ArrayUuid)
			.ToList();

		// Create logical volumes for each complete RAID array
		foreach (var group in raidGroups) {
			var diskList = group.ToList();
			if (diskList.Count > 0) {
				var raidGroup = new LinuxRaidDiskGroup(diskList[0]);

				// Create logical volume for this RAID array
				foreach (var volume in raidGroup.GetVolumes()) {
					var lvi = new LogicalVolumeInfo(
						volume.Guid,
						raidGroup.FirstDisk.PhysicalVolume,
						volume.Open,
						volume.Length,
						volume.BiosType,
						volume.Status,
						GetTypeAsString(raidGroup.RaidLevel));
					result.Add(lvi.Identity, lvi);
				}
			}
		}
	}

	private static bool IsLinuxRaidPartition(PartitionInfo partition) {
		// Check for Linux RAID partition types
		return partition.BiosType == BiosPartitionTypes.LinuxRaidAutoDetect ||
			   partition.GuidType == GuidPartitionTypes.LinuxRaid;
	}

	private static string GetTypeAsString(uint raidLevel) {
		return raidLevel switch {
			1 => "Linux RAID 1 (Mirror)",
			_ => $"Linux RAID {raidLevel}"
		};
	}
}
