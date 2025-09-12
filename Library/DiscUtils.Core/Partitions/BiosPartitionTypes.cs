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

namespace DiscUtils.Partitions;

/// <summary>
/// Convenient access to well-known BIOS (MBR) Partition Types.
/// </summary>
public static class BiosPartitionTypes
{
    /// <summary>
    /// Microsoft FAT12 (fewer than 32,680 sectors in the volume).
    /// </summary>
    public const byte Fat12 = 0x01;

    /// <summary>
    /// Microsoft FAT16 (32,680–65,535 sectors or 16 MB–33 MB).
    /// </summary>
    public const byte Fat16Small = 0x04;

    /// <summary>
    /// Extended Partition (contains other partitions).
    /// </summary>
    public const byte Extended = 0x05;

    /// <summary>
    /// Microsoft BIGDOS FAT16 (33 MB–4 GB).
    /// </summary>
    public const byte Fat16 = 0x06;

    /// <summary>
    /// Installable File System (NTFS).
    /// </summary>
    public const byte Ntfs = 0x07;

    /// <summary>
    /// Microsoft FAT32.
    /// </summary>
    public const byte Fat32 = 0x0B;

    /// <summary>
    /// Microsoft FAT32, accessed using Int13h BIOS LBA extensions.
    /// </summary>
    public const byte Fat32Lba = 0x0C;

    /// <summary>
    /// Microsoft BIGDOS FAT16, accessed using Int13h BIOS LBA extensions.
    /// </summary>
    public const byte Fat16Lba = 0x0E;

    /// <summary>
    /// Extended Partition (contains other partitions), accessed using Int13h BIOS LBA extensions.
    /// </summary>
    public const byte ExtendedLba = 0x0F;

    /// <summary>
    /// Windows Logical Disk Manager dynamic volume.
    /// </summary>
    public const byte WindowsDynamicVolume = 0x42;

    /// <summary>
    /// Linux Swap.
    /// </summary>
    public const byte LinuxSwap = 0x82;

    /// <summary>
    /// Linux Native (ext2 and friends).
    /// </summary>
    public const byte LinuxNative = 0x83;

    /// <summary>
    /// Linux Logical Volume Manager (LVM).
    /// </summary>
    public const byte LinuxLvm = 0x8E;

    /// <summary>
    /// GUID Partition Table (GPT) protective partition, fills entire disk.
    /// </summary>
    public const byte GptProtective = 0xEE;

    /// <summary>
    /// EFI System partition on an MBR disk.
    /// </summary>
    public const byte EfiSystem = 0xEF;

	/// <summary>
    /// Linux Raid Auto Detect
    /// </summary>
    public const byte LinuxRaidAutoDetect = 0xFD;

    /// <summary>
    /// Provides a string representation of some known BIOS partition types.
    /// </summary>
    /// <param name="type">The partition type to represent as a string.</param>
    /// <returns>The string representation.</returns>
    public static string ToString(byte type)
    {
        return type switch
        {
            0x00 => "Unused",
            0x01 => "FAT12",
            0x02 => "XENIX root",
            0x03 => "XENIX /usr",
            0x04 => "FAT16 (<32M)",
            0x05 => "Extended (non-LBA)",
            0x06 => "FAT16 (>32M)",
            0x07 => "IFS (NTFS or HPFS)",
            0x0B => "FAT32 (non-LBA)",
            0x0C => "FAT32 (LBA)",
            0x0E => "FAT16 (LBA)",
            0x0F => "Extended (LBA)",
            0x11 => "Hidden FAT12",
            0x12 => "Vendor Config/Recovery/Diagnostics",
            0x14 => "Hidden FAT16 (<32M)",
            0x16 => "Hidden FAT16 (>32M)",
            0x17 => "Hidden IFS (NTFS or HPFS)",
            0x1B => "Hidden FAT32 (non-LBA)",
            0x1C => "Hidden FAT32 (LBA)",
            0x1E => "Hidden FAT16 (LBA)",
            0x27 => "Windows Recovery Environment",
            0x42 => "Windows Dynamic Volume",
            0x80 => "Minix v1.1 - v1.4a",
            0x81 => "Minix / Early Linux",
            0x82 => "Linux Swap",
            0x83 => "Linux Native",
            0x84 => "Hibernation",
            0x8E => "Linux LVM",
            0xA0 => "Laptop Hibernation",
            0xA8 => "Mac OS-X",
            0xAB => "Mac OS-X Boot",
            0xAF => "Mac OS-X HFS",
            0xC0 => "NTFT",
            0xDE => "Dell OEM",
            0xEE => "GPT Protective",
            0xEF => "EFI",
            0xFB => "VMware File System",
            0xFC => "VMware Swap",
			0xFD => "Linux Raid Auto Detect",
            0xFE => "IBM OEM",
            _ => $"Unknown 0x{type:X2}",
        };
    }
}