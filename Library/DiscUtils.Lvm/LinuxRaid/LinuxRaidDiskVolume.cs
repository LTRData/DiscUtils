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

namespace DiscUtils.Lvm.LinuxRaid;

internal class LinuxRaidDiskVolume : IDiagnosticTraceable {
	private readonly PhysicalVolumeInfo _physicalVolume;
	private readonly LinuxRaidSuperblock _superblock;

	internal LinuxRaidDiskVolume(PhysicalVolumeInfo physicalVolume, LinuxRaidSuperblock superblock) {
		_physicalVolume = physicalVolume;
		_superblock = superblock;
		if (_superblock?.IsValid != true)
			throw new InvalidDataException("Invalid Linux RAID superblock");
	}

	public PhysicalVolumeInfo PhysicalVolume => _physicalVolume;

	public LinuxRaidSuperblock Superblock => _superblock;

	public long DataOffset => (long)_superblock.DataOffset * Sizes.Sector;

	public Guid ArrayUuid => _superblock.ArrayUuid;

	public uint RaidLevel => _superblock.RaidLevel;

	public string ArrayName => _superblock.ArrayName;

	public ulong ArraySize => _superblock.ArraySize;

	public PartitionInfo Partition => _physicalVolume.Partition;

	public void Dump(TextWriter writer, string linePrefix) {
		writer.WriteLine($"{linePrefix}LINUX RAID DISK ({_superblock.ArrayName})");
		writer.WriteLine($"{linePrefix}      RAID Version: {_superblock.MajorVersion}.{_superblock.MinorVersion}");
		writer.WriteLine($"{linePrefix}        RAID Level: {_superblock.RaidLevel}");
		writer.WriteLine($"{linePrefix}        Array UUID: {_superblock.ArrayUuid}");
		writer.WriteLine($"{linePrefix}        Array Name: {_superblock.ArrayName}");
		writer.WriteLine($"{linePrefix}       Data Offset: {_superblock.DataOffset} (Sectors)");
		writer.WriteLine($"{linePrefix}        Array Size: {_superblock.ArraySize} (Sectors)");
		writer.WriteLine($"{linePrefix}       Total Disks: {_superblock.TotalDisks}");
	}

	public enum MetadataVersion {
		Version09,
		Version10,
		Version11,
		Version12
	}

	internal static LinuxRaidSuperblock GetSuperblock(PartitionInfo partition) {
		using var volumeStream = partition.Open();
		var superblock = new LinuxRaidSuperblock();
		Span<byte> buffer = stackalloc byte[4096]; // Large enough for any superblock

		// Get superblock locations for the volume (these are relative to volume start, not disk start)
		var locations = GetSuperblockLocations(partition);

		foreach (var (version, offset) in locations) {
			try {
				volumeStream.Position = offset;
				var bytesRead = volumeStream.Read(buffer);
				if (bytesRead >= 512) // Minimum superblock size
				{
					superblock.ReadFrom(buffer.Slice(0, bytesRead), version);
					if (superblock.IsValid) {
						return superblock;
					}
				}
			} catch {
				// Continue to next location if this one fails
			}
		}

		return null; // No valid superblock found
	}


	private static (MetadataVersion version, long offset)[] GetSuperblockLocations(PartitionInfo partition) {
		var volumeSize = partition.SectorCount;
		const int sectorSize = 512; // Standard sector size

		return
		[
            // v1.1 - at the beginning of the volume (block 0)
            (MetadataVersion.Version11, 0L),
            
            // v1.2 - at 4KB offset
            (MetadataVersion.Version12, 4096L),
            
            // v1.0 - at the end of the volume minus 8KB, then aligned to 64KB boundary
            (MetadataVersion.Version10, AlignDown(volumeSize - 8192, 65536)),
            
            // v0.9 - near the end of the volume
            (MetadataVersion.Version09, AlignDown(volumeSize - 65536, sectorSize))
		];
	}

	private static long AlignDown(long value, long alignment) {
		return Math.Max(0, value / alignment * alignment);
	}
}
