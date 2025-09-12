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

namespace DiscUtils.Lvm.LinuxRaid;

internal class LinuxRaidSuperblock {
	// Magic identifier for Linux MD RAID
	public const uint LinuxRaidMagic = 0xa92b4efc;

	public uint Magic { get; private set; }
	public uint MajorVersion { get; private set; }
	public uint MinorVersion { get; private set; }
	public uint RaidLevel { get; private set; }
	public Guid ArrayUuid { get; private set; }
	public string ArrayName { get; private set; }
	public ulong DataOffset { get; private set; }
	public ulong ArraySize { get; private set; }
	public uint TotalDisks { get; private set; }

	public bool IsValid => Magic == LinuxRaidMagic;

	public void ReadFrom(ReadOnlySpan<byte> buffer, LinuxRaidDiskVolume.MetadataVersion version) {
		Magic = EndianUtilities.ToUInt32LittleEndian(buffer);
		if (!IsValid)
			return;
		MinorVersion = version switch {
			LinuxRaidDiskVolume.MetadataVersion.Version09 => 9,
			LinuxRaidDiskVolume.MetadataVersion.Version10 => 0,
			LinuxRaidDiskVolume.MetadataVersion.Version11 => 1,
			LinuxRaidDiskVolume.MetadataVersion.Version12 => 2,
			_ => throw new InvalidOperationException($"Unexpected version {version}")
		};
		if (MinorVersion == 9)
			ReadVersion09(buffer);
		else
			ReadVersion1x(buffer);
	}
	/*  Not sure why finding the 0.9 docs are hard for the spec but grub ( https://git.proxmox.com/?p=grub2.git;a=blob_plain;f=disk/mdraid_linux.c;hb=ee293aee1b13d9c1d1124226ecf95e1a70e4a936 ) provides it pretty clearly:
	   grub_uint32_t md_magic;	//0 MD identifier.  
grub_uint32_t major_version;	//1 Major version. 
grub_uint32_t minor_version;	//2 Minor version.  
grub_uint32_t patch_version;	//3 Patchlevel version.  
grub_uint32_t gvalid_words;	//4 Number of used words in this section.  
grub_uint32_t set_uuid0;	//5 Raid set identifier.  
grub_uint32_t ctime;		//6 Creation time.  
grub_uint32_t level;		//7 Raid personality.  
grub_uint32_t size;		//8 Apparent size of each individual disk.  
grub_uint32_t nr_disks;	//9 Total disks in the raid set.  
grub_uint32_t raid_disks;	//10 Disks in a fully functional raid set.  
grub_uint32_t md_minor;	//11 Preferred MD minor device number.  
grub_uint32_t not_persistent;	//12 Does it have a persistent superblock.  
grub_uint32_t set_uuid1;	//13 Raid set identifier #2.  
grub_uint32_t set_uuid2;	//14 Raid set identifier #3.  
grub_uint32_t set_uuid3;	//15 Raid set identifier #4.  
grub_uint32_t gstate_creserved[SB_GENERIC_CONSTANT_WORDS - 16];
	*/
	private void ReadVersion09(ReadOnlySpan<byte> buffer) {
		var uIntSize = 4;
		DataOffset = 0;
		MajorVersion = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(uIntSize * 1));
		MinorVersion = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(uIntSize * 2));
		if (MajorVersion != 0 || MinorVersion != 9) {
			// Not actually version 0.9 despite being in the 0.9 location
			Magic = 0;
			return;
		}
		RaidLevel = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(uIntSize * 7));
		var _uuidBytes = new byte[16];
		Span<byte> uuidBytes = _uuidBytes;
		buffer.Slice(uIntSize * 5, 4).CopyTo(uuidBytes);
		buffer.Slice(uIntSize * 13, 4).CopyTo(uuidBytes.Slice(4));
		buffer.Slice(uIntSize * 14, 4).CopyTo(uuidBytes.Slice(8));
		buffer.Slice(uIntSize * 15, 4).CopyTo(uuidBytes.Slice(12));
		ArrayUuid = new Guid(_uuidBytes);
		ArrayName = "raid";
		ArraySize = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(uIntSize * 8)) * 1024UL;
		TotalDisks = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(uIntSize * 9));
	}

	private void ReadVersion1x(ReadOnlySpan<byte> buffer) {
		// Version 1.x format https://archive.kernel.org/oldwiki/raid.wiki.kernel.org/index.php/RAID_superblock_formats.html#Sub-versions_of_the_version-1_superblock
		MajorVersion = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x04));
		if (MajorVersion != 1) {
			// Not actually version 1.x despite being in the 1.x location
			Magic = 0;
			return;
		}

		// RAID level at offset 0x48
		RaidLevel = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x48));

		// Array UUID at offset 0x10-0x1F
		var uuidBytes = new byte[16];
		buffer.Slice(0x10, 16).CopyTo(uuidBytes);
		ArrayUuid = new Guid(uuidBytes);

		// Array name at offset 0x20-0x3F (32 bytes)
		ArrayName = EndianUtilities.BytesToZString(buffer.Slice(0x20, 32));

		// Data offset at offset 0x80 (8 bytes, little endian)
		DataOffset = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x80));

		// Array size at offset 0x88 (8 bytes, sectors)
		ArraySize = EndianUtilities.ToUInt64LittleEndian(buffer.Slice(0x88));

		// Total disks at offset 0x9C
		TotalDisks = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x5C));
	}
}
