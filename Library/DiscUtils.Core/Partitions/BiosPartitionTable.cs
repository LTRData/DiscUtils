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
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Partitions;

/// <summary>
/// Represents a BIOS (MBR) Partition Table.
/// </summary>
public sealed class BiosPartitionTable : PartitionTable
{
    private Stream _diskData;
    private Geometry _diskGeometry;

    /// <summary>
    /// Initializes a new instance of the BiosPartitionTable class.
    /// </summary>
    /// <param name="disk">The disk containing the partition table.</param>
    public BiosPartitionTable(VirtualDisk disk)
    {
        Init(disk.Content, disk.BiosGeometry);
    }

    /// <summary>
    /// Initializes a new instance of the BiosPartitionTable class.
    /// </summary>
    /// <param name="disk">The stream containing the disk data.</param>
    /// <param name="diskGeometry">The geometry of the disk.</param>
    public BiosPartitionTable(Stream disk, Geometry diskGeometry)
    {
        Init(disk, diskGeometry);
    }

    public Geometry DiskGeometry => _diskGeometry;

    /// <summary>
    /// Gets a collection of the partitions for storing Operating System file-systems.
    /// </summary>
    public ReadOnlyCollection<BiosPartitionInfo> BiosUserPartitions
    {
        get
        {
            var result = new List<BiosPartitionInfo>();
            foreach (var r in GetAllRecords())
            {
                if (r.IsValid)
                {
                    result.Add(new BiosPartitionInfo(this, r));
                }
            }

            return new ReadOnlyCollection<BiosPartitionInfo>(result);
        }
    }

    /// <summary>
    /// Gets the GUID that uniquely identifies this disk, if supported (else returns <c>null</c>).
    /// 
    /// This implementation returns a "pseudo-Guid" with first four bytes filled with disk signature bytes.
    /// </summary>
    public override Guid DiskGuid
    {
        get
        {
            _diskData.Position = 0;

            byte[] allocated = null;

            var bootSector = _diskGeometry.BytesPerSector <= 1024
                ? stackalloc byte[_diskGeometry.BytesPerSector]
                : (allocated = ArrayPool<byte>.Shared.Rent(_diskGeometry.BytesPerSector)).AsSpan(0, _diskGeometry.BytesPerSector);

            try
            {
                _diskData.ReadExactly(bootSector);

                Span<byte> guid = stackalloc byte[16];
                bootSector.Slice(0x1B8, 4).CopyTo(guid);
                guid.Slice(4).Clear();
                return EndianUtilities.ToGuidLittleEndian(guid);
            }
            finally
            {
                if (allocated is not null)
                {
                    ArrayPool<byte>.Shared.Return(allocated);
                }
            }
        }
    }

    /// <summary>
    /// Gets a collection of the partitions for storing Operating System file-systems.
    /// </summary>
    public override ReadOnlyCollection<PartitionInfo> Partitions
    {
        get
        {
            var result = new List<PartitionInfo>();
            foreach (var r in GetAllRecords())
            {
                if (r.IsValid)
                {
                    result.Add(new BiosPartitionInfo(this, r));
                }
            }

            return new ReadOnlyCollection<PartitionInfo>(result);
        }
    }

    /// <summary>
    /// Makes a best guess at the geometry of a disk.
    /// </summary>
    /// <param name="disk">String containing the disk image to detect the geometry from.</param>
    /// <returns>The detected geometry.</returns>
    public static Geometry DetectGeometry(Stream disk)
    {
        if (disk.Length >= Sizes.Sector)
        {
            disk.Position = 0;
            Span<byte> bootSector = stackalloc byte[Sizes.Sector];
            disk.ReadExactly(bootSector);
            if (bootSector[510] == 0x55 && bootSector[511] == 0xAA)
            {
                byte maxHead = 0;
                byte maxSector = 0;
                foreach (var record in ReadPrimaryRecords(bootSector))
                {
                    maxHead = Math.Max(maxHead, record.EndHead);
                    maxSector = Math.Max(maxSector, record.EndSector);
                }

                if (maxHead > 0 && maxSector > 0)
                {
                    var cylSize = (maxHead + 1) * maxSector * 512;
                    return new Geometry((int)MathUtilities.Ceil(disk.Length, cylSize), maxHead + 1, maxSector);
                }
            }
        }

        return Geometry.FromCapacity(disk.Length);
    }

    /// <summary>
    /// Indicates if a stream contains a valid partition table.
    /// </summary>
    /// <param name="disk">The stream to inspect.</param>
    /// <returns><c>true</c> if the partition table is valid, else <c>false</c>.</returns>
    public static bool IsValid(Stream disk)
    {
        if (disk.Length < Sizes.Sector)
        {
            return false;
        }

        disk.Position = 0;
        Span<byte> bootSector = stackalloc byte[Sizes.Sector];
        disk.ReadExactly(bootSector);

        // Check for the 'bootable sector' marker
        if (bootSector[510] != 0x55 || bootSector[511] != 0xAA)
        {
            return false;
        }

        var knownPartitions = new List<StreamExtent>();
        foreach (var record in ReadPrimaryRecords(bootSector))
        {
            // If the partition extends beyond the end of the disk, this is probably an invalid partition table
            if (record.PartitionType != BiosPartitionTypes.GptProtective &&
                record.PartitionType != BiosPartitionTypes.Extended &&
                record.PartitionType != BiosPartitionTypes.ExtendedLba &&
                (record.LBAStart + (long)record.LBALength) * Sizes.Sector > disk.Length)
            {
                return false;
            }

            if (record.LBALength > 0)
            {
                var thisPartitionExtents = SingleValueEnumerable.Get(new StreamExtent(record.LBAStart, record.LBALength));

                // If the partition intersects another partition, this is probably an invalid partition table
                foreach (var overlap in StreamExtent.Intersect(knownPartitions, thisPartitionExtents))
                {
                    return false;
                }

                knownPartitions = new List<StreamExtent>(StreamExtent.Union(knownPartitions, thisPartitionExtents));
            }
        }

        return true;
    }

    /// <summary>
    /// Creates a new partition table on a disk.
    /// </summary>
    /// <param name="disk">The disk to initialize.</param>
    /// <returns>An object to access the newly created partition table.</returns>
    public static BiosPartitionTable Initialize(VirtualDisk disk)
    {
        return Initialize(disk.Content, disk.BiosGeometry);
    }

    /// <summary>
    /// Creates a new partition table on a disk containing a single partition.
    /// </summary>
    /// <param name="disk">The disk to initialize.</param>
    /// <param name="type">The partition type for the single partition.</param>
    /// <returns>An object to access the newly created partition table.</returns>
    public static BiosPartitionTable Initialize(VirtualDisk disk, WellKnownPartitionType type)
    {
        var table = Initialize(disk.Content, disk.BiosGeometry);
        table.Create(type, true);
        return table;
    }

    /// <summary>
    /// Creates a new partition table on a disk.
    /// </summary>
    /// <param name="disk">The stream containing the disk data.</param>
    /// <param name="diskGeometry">The geometry of the disk.</param>
    /// <returns>An object to access the newly created partition table.</returns>
    public static BiosPartitionTable Initialize(Stream disk, Geometry diskGeometry)
    {
        var data = disk;

        byte[] allocated = null;

        var bootSector = diskGeometry.BytesPerSector <= 1024
            ? stackalloc byte[diskGeometry.BytesPerSector]
            : (allocated = ArrayPool<byte>.Shared.Rent(diskGeometry.BytesPerSector)).AsSpan(0, diskGeometry.BytesPerSector);

        try
        {
            if (data.Length >= diskGeometry.BytesPerSector)
            {
                data.Position = 0;
                data.ReadExactly(bootSector);

                // Wipe all four 16-byte partition table entries
                bootSector.Slice(0x01BE, 16 * 4).Clear();
            }
            else
            {
                bootSector.Clear();
            }

            // Marker bytes
            bootSector[510] = 0x55;
            bootSector[511] = 0xAA;

            data.Position = 0;
            data.Write(bootSector);

            return new BiosPartitionTable(disk, diskGeometry);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    /// <summary>
    /// Creates a new partition that encompasses the entire disk.
    /// </summary>
    /// <param name="type">The partition type.</param>
    /// <param name="active">Whether the partition is active (bootable).</param>
    /// <returns>The index of the partition.</returns>
    /// <remarks>The partition table must be empty before this method is called,
    /// otherwise IOException is thrown.</remarks>
    public override int Create(WellKnownPartitionType type, bool active)
    {
        var allocationGeometry = new Geometry(_diskData.Length, _diskGeometry.HeadsPerCylinder,
            _diskGeometry.SectorsPerTrack, _diskGeometry.BytesPerSector);

        var start = new ChsAddress(0, 1, 1);
        var last = allocationGeometry.LastSector;

        var startLba = allocationGeometry.ToLogicalBlockAddress(start);
        var lastLba = allocationGeometry.ToLogicalBlockAddress(last);

        return CreatePrimaryByCylinder(0, allocationGeometry.Cylinders - 1,
            ConvertType(type, (lastLba - startLba) * _diskGeometry.BytesPerSector), active);
    }

    /// <summary>
    /// Creates a new primary partition with a target size.
    /// </summary>
    /// <param name="size">The target size (in bytes).</param>
    /// <param name="type">The partition type.</param>
    /// <param name="active">Whether the partition is active (bootable).</param>
    /// <returns>The index of the new partition.</returns>
    public override int Create(long size, WellKnownPartitionType type, bool active)
    {
        var cylinderCapacity = _diskGeometry.SectorsPerTrack * _diskGeometry.HeadsPerCylinder *
                               _diskGeometry.BytesPerSector;
        var numCylinders = (int)(size / cylinderCapacity);

        var startCylinder = FindCylinderGap(numCylinders);

        return CreatePrimaryByCylinder(startCylinder, startCylinder + numCylinders - 1, ConvertType(type, size),
            active);
    }

    /// <summary>
    /// Creates a new aligned partition that encompasses the entire disk.
    /// </summary>
    /// <param name="type">The partition type.</param>
    /// <param name="active">Whether the partition is active (bootable).</param>
    /// <param name="alignment">The alignment (in bytes).</param>
    /// <returns>The index of the partition.</returns>
    /// <remarks>The partition table must be empty before this method is called,
    /// otherwise IOException is thrown.</remarks>
    /// <remarks>
    /// Traditionally partitions were aligned to the physical structure of the underlying disk,
    /// however with modern storage greater efficiency is acheived by aligning partitions on
    /// large values that are a power of two.
    /// </remarks>
    public override int CreateAligned(WellKnownPartitionType type, bool active, int alignment)
    {
        var allocationGeometry = new Geometry(_diskData.Length, _diskGeometry.HeadsPerCylinder,
            _diskGeometry.SectorsPerTrack, _diskGeometry.BytesPerSector);

        var start = new ChsAddress(0, 1, 1);

        var startLba = MathUtilities.RoundUp(allocationGeometry.ToLogicalBlockAddress(start),
            alignment / _diskGeometry.BytesPerSector);
        var lastLba = MathUtilities.RoundDown(_diskData.Length / _diskGeometry.BytesPerSector,
            alignment / _diskGeometry.BytesPerSector);

        return CreatePrimaryBySector(startLba, lastLba - 1,
            ConvertType(type, (lastLba - startLba) * _diskGeometry.BytesPerSector), active);
    }

    /// <summary>
    /// Creates a new aligned partition with a target size.
    /// </summary>
    /// <param name="size">The target size (in bytes).</param>
    /// <param name="type">The partition type.</param>
    /// <param name="active">Whether the partition is active (bootable).</param>
    /// <param name="alignment">The alignment (in bytes).</param>
    /// <returns>The index of the new partition.</returns>
    /// <remarks>
    /// Traditionally partitions were aligned to the physical structure of the underlying disk,
    /// however with modern storage greater efficiency is achieved by aligning partitions on
    /// large values that are a power of two.
    /// </remarks>
    public override int CreateAligned(long size, WellKnownPartitionType type, bool active, int alignment)
    {
        if (size < _diskGeometry.BytesPerSector)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "size must be at least one sector");
        }

        if (alignment % _diskGeometry.BytesPerSector != 0)
        {
            throw new ArgumentException("Alignment is not a multiple of the sector size");
        }

        if (size % alignment != 0)
        {
            throw new ArgumentException("Size is not a multiple of the alignment");
        }

        var sectorLength = size / _diskGeometry.BytesPerSector;
        var start = FindGap(size / _diskGeometry.BytesPerSector, alignment / _diskGeometry.BytesPerSector);

        return CreatePrimaryBySector(start, start + sectorLength - 1,
            ConvertType(type, sectorLength * _diskGeometry.BytesPerSector), active);
    }

    /// <summary>
    /// Deletes a partition at a given index.
    /// </summary>
    /// <param name="index">The index of the partition.</param>
    public override void Delete(int index)
    {
        WriteRecord(index, new BiosPartitionRecord());
    }

    /// <summary>
    /// Creates a new Primary Partition that occupies whole cylinders, for best compatibility.
    /// </summary>
    /// <param name="first">The first cylinder to include in the partition (inclusive).</param>
    /// <param name="last">The last cylinder to include in the partition (inclusive).</param>
    /// <param name="type">The BIOS (MBR) type of the new partition.</param>
    /// <param name="markActive">Whether to mark the partition active (bootable).</param>
    /// <returns>The index of the new partition.</returns>
    /// <remarks>If the cylinder 0 is given, the first track will not be used, to reserve space
    /// for the meta-data at the start of the disk.</remarks>
    public int CreatePrimaryByCylinder(int first, int last, byte type, bool markActive)
    {
        if (first < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(first), first, "First cylinder must be Zero or greater");
        }

        if (last <= first)
        {
            throw new ArgumentException("Last cylinder must be greater than first");
        }

        var lbaStart = first == 0
            ? _diskGeometry.ToLogicalBlockAddress(0, 1, 1)
            : _diskGeometry.ToLogicalBlockAddress(first, 0, 1);
        var lbaLast = _diskGeometry.ToLogicalBlockAddress(last, _diskGeometry.HeadsPerCylinder - 1,
            _diskGeometry.SectorsPerTrack);

        return CreatePrimaryBySector(lbaStart, lbaLast, type, markActive);
    }

    /// <summary>
    /// Creates a new Primary Partition, specified by Logical Block Addresses.
    /// </summary>
    /// <param name="first">The LBA address of the first sector (inclusive).</param>
    /// <param name="last">The LBA address of the last sector (inclusive).</param>
    /// <param name="type">The BIOS (MBR) type of the new partition.</param>
    /// <param name="markActive">Whether to mark the partition active (bootable).</param>
    /// <returns>The index of the new partition.</returns>
    public int CreatePrimaryBySector(long first, long last, byte type, bool markActive)
    {
        if (first >= last)
        {
            throw new ArgumentException("The first sector in a partition must be before the last");
        }

        if ((last + 1) * _diskGeometry.BytesPerSector > _diskData.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(last), last,
                "The last sector extends beyond the end of the disk");
        }

        var existing = GetPrimaryRecords();

        var newRecord = new BiosPartitionRecord();
        var startAddr = _diskGeometry.ToChsAddress(first);
        var endAddr = _diskGeometry.ToChsAddress(last);

        // Because C/H/S addresses can max out at lower values than the LBA values,
        // the special tuple (1023, 254, 63) is used.
        if (startAddr.Cylinder > 1023)
        {
            startAddr = new ChsAddress(1023, 254, 63);
        }

        if (endAddr.Cylinder > 1023)
        {
            endAddr = new ChsAddress(1023, 254, 63);
        }

        newRecord.StartCylinder = (ushort)startAddr.Cylinder;
        newRecord.StartHead = (byte)startAddr.Head;
        newRecord.StartSector = (byte)startAddr.Sector;
        newRecord.EndCylinder = (ushort)endAddr.Cylinder;
        newRecord.EndHead = (byte)endAddr.Head;
        newRecord.EndSector = (byte)endAddr.Sector;
        newRecord.LBAStart = (uint)first;
        newRecord.LBALength = (uint)(last - first + 1);
        newRecord.PartitionType = type;
        newRecord.Status = (byte)(markActive ? 0x80 : 0x00);

        // First check for overlap with existing partition...
        foreach (var r in existing)
        {
            if (Utilities.RangesOverlap((uint)first, (uint)last + 1, r.LBAStartAbsolute,
                r.LBAStartAbsolute + r.LBALength))
            {
                throw new IOException("New partition overlaps with existing partition");
            }
        }

        // Now look for empty partition
        for (var i = 0; i < 4; ++i)
        {
            if (!existing[i].IsValid)
            {
                WriteRecord(i, newRecord);
                return i;
            }
        }

        throw new IOException("No primary partition slots available");
    }

    /// <summary>
    /// Sets the active partition.
    /// </summary>
    /// <param name="index">The index of the primary partition to mark bootable, or <c>-1</c> for none.</param>
    /// <remarks>The supplied index is the index within the primary partition, see <c>PrimaryIndex</c> on <c>BiosPartitionInfo</c>.</remarks>
    public void SetActivePartition(int index)
    {
        var records = GetPrimaryRecords();

        for (var i = 0; i < records.Length; ++i)
        {
            var new_status = i == index ? (byte)0x80 : (byte)0x00;
            if (records[i].Status != new_status)
            {
                records[i].Status = new_status;
                WriteRecord(i, records[i]);
            }
        }
    }

    /// <summary>
    /// Gets all of the disk ranges containing partition table metadata.
    /// </summary>
    /// <returns>Set of stream extents, indicated as byte offset from the start of the disk.</returns>
    public IEnumerable<StreamExtent> GetMetadataDiskExtents()
    {
        yield return new StreamExtent(0, Sizes.Sector);

        foreach (var primaryRecord in GetPrimaryRecords())
        {
            if (primaryRecord.IsValid)
            {
                if (IsExtendedPartition(primaryRecord))
                {
                    foreach (var extent in new BiosExtendedPartitionTable(_diskData, primaryRecord.LBAStart, _diskGeometry).GetMetadataDiskExtents())
                    {
                        yield return extent;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Updates the CHS fields in partition records to reflect a new BIOS geometry.
    /// </summary>
    /// <param name="geometry">The disk's new BIOS geometry.</param>
    /// <remarks>The partitions are not relocated to a cylinder boundary, just the CHS fields are updated on the
    /// assumption the LBA fields are definitive.</remarks>
    public void UpdateBiosGeometry(Geometry geometry)
    {
        _diskData.Position = 0;

        byte[] allocated = null;

        var bootSector = _diskGeometry.BytesPerSector <= 1024
            ? stackalloc byte[_diskGeometry.BytesPerSector]
            : (allocated = ArrayPool<byte>.Shared.Rent(_diskGeometry.BytesPerSector)).AsSpan(0, _diskGeometry.BytesPerSector);

        try
        {
            _diskData.ReadExactly(bootSector);

            var records = ReadPrimaryRecords(bootSector);

            for (var i = 0; i < records.Length; ++i)
            {
                var record = records[i];
                if (record.IsValid)
                {
                    var newStartAddress = geometry.ToChsAddress(record.LBAStartAbsolute);
                    if (newStartAddress.Cylinder > 1023)
                    {
                        newStartAddress = new ChsAddress(1023, geometry.HeadsPerCylinder - 1, geometry.SectorsPerTrack);
                    }

                    var newEndAddress = geometry.ToChsAddress(record.LBAStartAbsolute + record.LBALength - 1);
                    if (newEndAddress.Cylinder > 1023)
                    {
                        newEndAddress = new ChsAddress(1023, geometry.HeadsPerCylinder - 1, geometry.SectorsPerTrack);
                    }

                    record.StartCylinder = (ushort)newStartAddress.Cylinder;
                    record.StartHead = (byte)newStartAddress.Head;
                    record.StartSector = (byte)newStartAddress.Sector;
                    record.EndCylinder = (ushort)newEndAddress.Cylinder;
                    record.EndHead = (byte)newEndAddress.Head;
                    record.EndSector = (byte)newEndAddress.Sector;

                    WriteRecord(i, record);
                }
            }

            _diskGeometry = geometry;
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    internal SparseStream Open(BiosPartitionRecord record)
    {
        return new SubStream(_diskData, Ownership.None,
            record.LBAStartAbsolute * _diskGeometry.BytesPerSector,
            record.LBALength * _diskGeometry.BytesPerSector);
    }

    private static BiosPartitionRecord[] ReadPrimaryRecords(ReadOnlySpan<byte> bootSector)
    {
        var records = new BiosPartitionRecord[4];
        for (var i = 0; i < 4; ++i)
        {
            records[i] = new BiosPartitionRecord(bootSector.Slice(0x01BE + i * 0x10), 0, i);
        }

        return records;
    }

    private static bool IsExtendedPartition(BiosPartitionRecord r)
    {
        return r.PartitionType == BiosPartitionTypes.Extended || r.PartitionType == BiosPartitionTypes.ExtendedLba;
    }

    private static byte ConvertType(WellKnownPartitionType type, long size)
    {
        switch (type)
        {
            case WellKnownPartitionType.WindowsFat:
                if (size < 512 * Sizes.OneMiB)
                {
                    return BiosPartitionTypes.Fat16;
                }

                if (size < 1023 * (long)254 * 63 * 512)
                {
                    // Max BIOS size
                    return BiosPartitionTypes.Fat32;
                }

                return BiosPartitionTypes.Fat32Lba;

            case WellKnownPartitionType.WindowsNtfs:
                return BiosPartitionTypes.Ntfs;
            case WellKnownPartitionType.Linux:
                return BiosPartitionTypes.LinuxNative;
            case WellKnownPartitionType.LinuxSwap:
                return BiosPartitionTypes.LinuxSwap;
            case WellKnownPartitionType.LinuxLvm:
                return BiosPartitionTypes.LinuxLvm;
            default:
                throw new ArgumentException(
                    $"Unrecognized partition type: '{type}'",
                    nameof(type));
        }
    }

    private IEnumerable<BiosPartitionRecord> GetAllRecords()
    {
        foreach (var primaryRecord in GetPrimaryRecords())
        {
            if (primaryRecord.IsValid)
            {
                if (IsExtendedPartition(primaryRecord))
                {
                    foreach (var record in GetExtendedRecords(primaryRecord))
                    {
                        yield return record;
                    }
                }
                else
                {
                    yield return primaryRecord;
                }
            }
        }
    }

    private BiosPartitionRecord[] GetPrimaryRecords()
    {
        _diskData.Position = 0;

        byte[] allocated = null;

        var bootSector = _diskGeometry.BytesPerSector <= 1024
            ? stackalloc byte[_diskGeometry.BytesPerSector]
            : (allocated = ArrayPool<byte>.Shared.Rent(_diskGeometry.BytesPerSector)).AsSpan(0, _diskGeometry.BytesPerSector);

        try
        {
            _diskData.ReadExactly(bootSector);

            return ReadPrimaryRecords(bootSector);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    private IEnumerable<BiosPartitionRecord> GetExtendedRecords(BiosPartitionRecord r)
    {
        return new BiosExtendedPartitionTable(_diskData, r.LBAStart, _diskGeometry).GetPartitions();
    }

    private void WriteRecord(int i, BiosPartitionRecord newRecord)
    {
        _diskData.Position = 0;

        byte[] allocated = null;

        var bootSector = _diskGeometry.BytesPerSector <= 1024
            ? stackalloc byte[_diskGeometry.BytesPerSector]
            : (allocated = ArrayPool<byte>.Shared.Rent(_diskGeometry.BytesPerSector)).AsSpan(0, _diskGeometry.BytesPerSector);

        try
        {
            _diskData.ReadExactly(bootSector);

            newRecord.WriteTo(bootSector.Slice(0x01BE + i * 16));
            _diskData.Position = 0;
            _diskData.Write(bootSector);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    private int FindCylinderGap(int numCylinders)
    {
        var list = GetPrimaryRecords().Where(r => r.IsValid).ToList();
        list.Sort();

        var startCylinder = 0;
        foreach (var r in list)
        {
            int existingStart = r.StartCylinder;
            int existingEnd = r.EndCylinder;

            // LBA can represent bigger disk locations than CHS, so assume the LBA to be definitive in the case where it
            // appears the CHS address has been truncated.
            if (r.LBAStart > _diskGeometry.ToLogicalBlockAddress(r.StartCylinder, r.StartHead, r.StartSector))
            {
                existingStart = _diskGeometry.ToChsAddress((int)r.LBAStart).Cylinder;
            }

            if (r.LBAStart + r.LBALength >
                _diskGeometry.ToLogicalBlockAddress(r.EndCylinder, r.EndHead, r.EndSector))
            {
                existingEnd = _diskGeometry.ToChsAddress((int)(r.LBAStart + r.LBALength)).Cylinder;
            }

            if (
                !Utilities.RangesOverlap(startCylinder, startCylinder + numCylinders - 1, existingStart, existingEnd))
            {
                break;
            }

            startCylinder = existingEnd + 1;
        }

        return startCylinder;
    }

    private long FindGap(long numSectors, long alignmentSectors)
    {
        var list = GetPrimaryRecords().Where(r => r.IsValid).ToList();
        list.Sort();

        var startSector = MathUtilities.RoundUp(_diskGeometry.ToLogicalBlockAddress(0, 1, 1), alignmentSectors);

        var idx = 0;
        while (idx < list.Count)
        {
            var entry = list[idx];
            while (idx < list.Count && startSector >= entry.LBAStartAbsolute + entry.LBALength)
            {
                idx++;
                entry = list[idx];
            }

            if (Utilities.RangesOverlap(startSector, startSector + numSectors, entry.LBAStartAbsolute,
                entry.LBAStartAbsolute + entry.LBALength))
            {
                startSector = MathUtilities.RoundUp(entry.LBAStartAbsolute + entry.LBALength, alignmentSectors);
            }

            idx++;
        }

        if (_diskGeometry.TotalSectorsLong - startSector < numSectors)
        {
            throw new IOException($"Unable to find free space of {numSectors} sectors");
        }

        return startSector;
    }

    private void Init(Stream disk, Geometry diskGeometry)
    {
        _diskData = disk;
        _diskGeometry = diskGeometry;

        _diskData.Position = 0;

        byte[] allocated = null;

        var bootSector = _diskGeometry.BytesPerSector <= 1024
            ? stackalloc byte[_diskGeometry.BytesPerSector]
            : (allocated = ArrayPool<byte>.Shared.Rent(_diskGeometry.BytesPerSector)).AsSpan(0, _diskGeometry.BytesPerSector);

        try
        {
            _diskData.ReadExactly(bootSector);

            if (bootSector[510] != 0x55 || bootSector[511] != 0xAA)
            {
                throw new IOException("Invalid boot sector - no magic number 0xAA55");
            }
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }
}