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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscUtils.Streams;


namespace DiscUtils.Lvm.LinuxRaid;
using LogicalVolumeStatus=DiscUtils.LogicalVolumeStatus;
internal class LinuxRaidDiskGroup : IDiagnosticTraceable {
	private readonly List<LinuxRaidDiskVolume> _disks;
	private readonly Guid _arrayUuid;
	private readonly string _arrayName;
	private readonly uint _raidLevel;

	internal LinuxRaidDiskGroup(LinuxRaidDiskVolume initialDisk) {
		_disks = [initialDisk];
		_arrayUuid = initialDisk.ArrayUuid;
		_arrayName = initialDisk.ArrayName;
		_raidLevel = initialDisk.RaidLevel;
	}

	public Guid ArrayUuid => _arrayUuid;
	public string ArrayName => _arrayName;
	public uint RaidLevel => _raidLevel;
	public int DiskCount => _disks.Count;

	// Property to access the first disk for volume calculations
	internal LinuxRaidDiskVolume FirstDisk => _disks.FirstOrDefault();

	public void Dump(TextWriter writer, string linePrefix) {
		writer.WriteLine($"{linePrefix}LINUX RAID ARRAY ({_arrayName})");
		writer.WriteLine($"{linePrefix}  Array UUID: {_arrayUuid}");
		writer.WriteLine($"{linePrefix}  Array Name: {_arrayName}");
		writer.WriteLine($"{linePrefix}  RAID Level: {_raidLevel}");
		writer.WriteLine($"{linePrefix}  Disk Count: {_disks.Count}");
		writer.WriteLine();

		writer.WriteLine($"{linePrefix}  MEMBER DISKS");
		for (var i = 0; i < _disks.Count; i++) {
			writer.WriteLine($"{linePrefix}    DISK {i}");
			_disks[i].Dump(writer, $"{linePrefix}      ");
		}
	}

	public void Add(LinuxRaidDiskVolume disk) {
		if (disk.ArrayUuid != _arrayUuid) {
			throw new InvalidOperationException("Cannot add disk with different array UUID to RAID group");
		}

		_disks.Add(disk);
	}

	internal IEnumerable<LinuxRaidVolume> GetVolumes() {
		yield return new LinuxRaidVolume(this);
	}

	internal LogicalVolumeStatus GetVolumeStatus() =>  LogicalVolumeStatus.Healthy;

	internal SparseStream OpenVolume() {
		switch (_raidLevel) {
			case 1: // RAID 1 - mirroring
				return OpenRaid1Volume();
			default:
				throw new NotSupportedException($"RAID level {_raidLevel} is not currently supported");
		}
	}





	private SparseStream OpenRaid1Volume() {
		// RAID 1: mirror data across disks - read from first available disk
		var memberStreams = _disks.Select(GetDiskStream).ToArray();

		// For RAID 1, we can use mirror stream to provide redundancy
		return new MirrorStream(Ownership.Dispose, memberStreams);
	}



	private SparseStream GetDiskStream(LinuxRaidDiskVolume disk) {
		// If the disk is from a partition, open the partition stream and offset within it
		if (disk.Partition != null) {
			var partitionStream = disk.Partition.Open();
			return new SubStream(partitionStream, Ownership.Dispose,
				disk.DataOffset, (long)disk.ArraySize * Sizes.Sector);
		} else {
			// For whole disk RAID, use the disk content directly
			return new SubStream(disk.PhysicalVolume.Open(), Ownership.Dispose,
			disk.DataOffset, (long)disk.ArraySize * Sizes.Sector);
		}
	}
}
