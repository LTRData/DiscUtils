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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Partitions;

/// <summary>
/// Represents a GUID Partition Table.
/// </summary>
public sealed class GuidPartitionTable : PartitionTable
{
    private Stream _diskData;
    private Geometry _diskGeometry;
    private byte[] _entryBuffer;
    private GptHeader _primaryHeader;
    private GptHeader _secondaryHeader;

    /// <summary>
    /// Initializes a new instance of the GuidPartitionTable class.
    /// </summary>
    /// <param name="disk">The disk containing the partition table.</param>
    public GuidPartitionTable(VirtualDisk disk)
    {
        Init(disk.Content, disk.Geometry
            ?? throw new InvalidOperationException("Unknown disk geometry"));
    }

    /// <summary>
    /// Initializes a new instance of the GuidPartitionTable class.
    /// </summary>
    /// <param name="disk">The stream containing the disk data.</param>
    /// <param name="diskGeometry">The geometry of the disk.</param>
    public GuidPartitionTable(Stream disk, Geometry diskGeometry)
    {
        Init(disk, diskGeometry);
    }

    /// <summary>
    /// Gets the unique GPT identifier for this disk.
    /// </summary>
    public override Guid DiskGuid => _primaryHeader.DiskGuid;

    /// <summary>
    /// Gets the first sector of the disk available to hold partitions.
    /// </summary>
    public long FirstUsableSector => _primaryHeader.FirstUsable;

    /// <summary>
    /// Gets the last sector of the disk available to hold partitions.
    /// </summary>
    public long LastUsableSector => _primaryHeader.LastUsable;

    /// <summary>
    /// Gets a collection of the partitions for storing Operating System file-systems.
    /// </summary>
    public override ReadOnlyCollection<PartitionInfo> Partitions => GetAllEntries().Select(e => new GuidPartitionInfo(this, e) as PartitionInfo).ToList().AsReadOnly();

    /// <summary>
    /// Creates a new partition table on a disk.
    /// </summary>
    /// <param name="disk">The disk to initialize.</param>
    /// <returns>An object to access the newly created partition table.</returns>
    public static GuidPartitionTable Initialize(VirtualDisk disk)
    {
        return Initialize(disk.Content, disk.Geometry
            ?? throw new InvalidOperationException("Unknown disk geometry"));
    }

    /// <summary>
    /// Creates a new partition table on a disk.
    /// </summary>
    /// <param name="disk">The stream containing the disk data.</param>
    /// <param name="diskGeometry">The geometry of the disk.</param>
    /// <returns>An object to access the newly created partition table.</returns>
    public static GuidPartitionTable Initialize(Stream disk, Geometry diskGeometry)
    {
        // Create the protective MBR partition record.
        var pt = BiosPartitionTable.Initialize(disk, diskGeometry);
        pt.CreatePrimaryByCylinder(0, diskGeometry.Cylinders - 1, BiosPartitionTypes.GptProtective, false);

        // Create the GPT headers, and blank-out the entry areas
        const int EntryCount = 128;
        const int EntrySize = 128;

        var entrySectors = (EntryCount * EntrySize + diskGeometry.BytesPerSector - 1) / diskGeometry.BytesPerSector;

        var entriesBuffer = new byte[EntryCount * EntrySize];

        // Prepare primary header
        var header = new GptHeader(diskGeometry.BytesPerSector)
        {
            HeaderLba = 1,
            AlternateHeaderLba = disk.Length / diskGeometry.BytesPerSector - 1
        };
        header.FirstUsable = header.HeaderLba + entrySectors + 1;
        header.LastUsable = header.AlternateHeaderLba - entrySectors - 1;
        header.DiskGuid = Guid.NewGuid();
        header.PartitionEntriesLba = 2;
        header.PartitionEntryCount = EntryCount;
        header.PartitionEntrySize = EntrySize;
        header.EntriesCrc = CalcEntriesCrc(entriesBuffer);

        // Write the primary header
        var headerBuffer = diskGeometry.BytesPerSector <= 1024
            ? stackalloc byte[diskGeometry.BytesPerSector]
            : new byte[diskGeometry.BytesPerSector];

        header.WriteTo(headerBuffer);
        disk.Position = header.HeaderLba * diskGeometry.BytesPerSector;
        disk.Write(headerBuffer);

        // Calc alternate header
        header.HeaderLba = header.AlternateHeaderLba;
        header.AlternateHeaderLba = 1;
        header.PartitionEntriesLba = header.HeaderLba - entrySectors;

        // Write the alternate header
        header.WriteTo(headerBuffer);
        disk.Position = header.HeaderLba * diskGeometry.BytesPerSector;
        disk.Write(headerBuffer);

        return new GuidPartitionTable(disk, diskGeometry);
    }

    /// <summary>
    /// Creates a new partition table on a disk containing a single partition.
    /// </summary>
    /// <param name="disk">The disk to initialize.</param>
    /// <param name="type">The partition type for the single partition.</param>
    /// <returns>An object to access the newly created partition table.</returns>
    public static GuidPartitionTable Initialize(VirtualDisk disk, WellKnownPartitionType type)
    {
        var pt = Initialize(disk);
        pt.Create(type, true);
        return pt;
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
        var allEntries = new List<GptEntry>(GetAllEntries());

        EstablishReservedPartition(allEntries);

        // Fill the rest of the disk with the requested partition
        var start = FirstAvailableSector(allEntries);
        var end = FindLastFreeSector(start, allEntries);

        return Create(start, end, GuidPartitionTypes.Convert(type), 0, "Data Partition");
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
        if (size < _diskGeometry.BytesPerSector)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "size must be at least one sector");
        }

        var sectorLength = size / _diskGeometry.BytesPerSector;
        var start = FindGap(size / _diskGeometry.BytesPerSector, 1);

        return Create(start, start + sectorLength - 1, GuidPartitionTypes.Convert(type), 0, "Data Partition");
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
        if (alignment % _diskGeometry.BytesPerSector != 0)
        {
            throw new ArgumentException("Alignment is not a multiple of the sector size");
        }

        var allEntries = new List<GptEntry>(GetAllEntries());

        EstablishReservedPartition(allEntries);

        // Fill the rest of the disk with the requested partition
        var start = MathUtilities.RoundUp(FirstAvailableSector(allEntries), alignment / _diskGeometry.BytesPerSector);
        var end = MathUtilities.RoundDown(FindLastFreeSector(start, allEntries) + 1,
            alignment / _diskGeometry.BytesPerSector);

        if (end <= start)
        {
            throw new IOException("No available space");
        }

        return Create(start, end - 1, GuidPartitionTypes.Convert(type), 0, "Data Partition");
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

        return Create(start, start + sectorLength - 1, GuidPartitionTypes.Convert(type), 0, "Data Partition");
    }

    /// <summary>
    /// Creates a new GUID partition on the disk.
    /// </summary>
    /// <param name="startSector">The first sector of the partition.</param>
    /// <param name="endSector">The last sector of the partition.</param>
    /// <param name="type">The partition type.</param>
    /// <param name="attributes">The partition attributes.</param>
    /// <param name="name">The name of the partition.</param>
    /// <returns>The index of the new partition.</returns>
    /// <remarks>No checking is performed on the parameters, the caller is
    /// responsible for ensuring that the partition does not overlap other partitions.</remarks>
    public int Create(long startSector, long endSector, Guid type, long attributes, string name)
    {
        var newEntry = CreateEntry(startSector, endSector, type, attributes, name);
        return GetEntryIndex(newEntry.Identity);
    }

    /// <summary>
    /// Deletes a partition at a given index.
    /// </summary>
    /// <param name="index">The index of the partition.</param>
    public override void Delete(int index)
    {
        var offset = GetPartitionOffset(index);
        Array.Clear(_entryBuffer, offset, _primaryHeader.PartitionEntrySize);
        Write();
    }

    internal SparseStream Open(GptEntry entry)
    {
        var start = entry.FirstUsedLogicalBlock * _diskGeometry.BytesPerSector;
        var end = (entry.LastUsedLogicalBlock + 1) * _diskGeometry.BytesPerSector;
        return new SubStream(_diskData, start, end - start);
    }

    private static uint CalcEntriesCrc(byte[] buffer)
    {
        return Crc32LittleEndian.Compute(Crc32Algorithm.Common, buffer, 0, buffer.Length);
    }

    private static int CountEntries<T>(ICollection<T> values, Func<T, bool> pred)
    {
        var count = 0;

        foreach (var val in values)
        {
            if (pred(val))
            {
                ++count;
            }
        }

        return count;
    }

    private void Init(Stream disk, Geometry diskGeometry)
    {
        BiosPartitionTable bpt;
        try
        {
            bpt = new BiosPartitionTable(disk, diskGeometry);
        }
        catch (IOException ioe)
        {
            throw new IOException("Invalid GPT disk, protective MBR table not present or invalid", ioe);
        }

        if (bpt.Count != 1 || bpt[0].BiosType != BiosPartitionTypes.GptProtective)
        {
            throw new IOException("Invalid GPT disk, protective MBR table is not valid");
        }

        _diskData = disk;
        _diskGeometry = diskGeometry;

        disk.Position = diskGeometry.BytesPerSector;
        var sector = diskGeometry.BytesPerSector <= 1024
            ? stackalloc byte[diskGeometry.BytesPerSector]
            : new byte[diskGeometry.BytesPerSector];
        
        disk.ReadExactly(sector);

        _primaryHeader = new GptHeader(diskGeometry.BytesPerSector);
        if (!_primaryHeader.ReadFrom(sector) || !ReadEntries(_primaryHeader))
        {
            disk.Position = disk.Length - diskGeometry.BytesPerSector;
            disk.ReadExactly(sector);
            _secondaryHeader = new GptHeader(diskGeometry.BytesPerSector);
            if (!_secondaryHeader.ReadFrom(sector) || !ReadEntries(_secondaryHeader))
            {
                throw new IOException("No valid GUID Partition Table found");
            }

            // Generate from the primary table from the secondary one
            _primaryHeader = new GptHeader(_secondaryHeader)
            {
                HeaderLba = _secondaryHeader.AlternateHeaderLba,
                AlternateHeaderLba = _secondaryHeader.HeaderLba,
                PartitionEntriesLba = 2
            };

            // If the disk is writable, fix up the primary partition table based on the
            // (valid) secondary table.
            if (disk.CanWrite)
            {
                WritePrimaryHeader();
            }
        }

        if (_secondaryHeader is null)
        {
            _secondaryHeader = new GptHeader(diskGeometry.BytesPerSector);
            disk.Position = disk.Length - diskGeometry.BytesPerSector;
            disk.ReadExactly(sector);
            if (!_secondaryHeader.ReadFrom(sector) || !ReadEntries(_secondaryHeader))
            {
                // Generate from the secondary table from the primary one
                _secondaryHeader = new GptHeader(_primaryHeader);
                _secondaryHeader.HeaderLba = _secondaryHeader.AlternateHeaderLba;
                _secondaryHeader.AlternateHeaderLba = _secondaryHeader.HeaderLba;
                _secondaryHeader.PartitionEntriesLba = _secondaryHeader.HeaderLba -
                                                       MathUtilities.RoundUp(
                                                           _secondaryHeader.PartitionEntryCount *
                                                           _secondaryHeader.PartitionEntrySize,
                                                           diskGeometry.BytesPerSector);

                // If the disk is writeable, fix up the secondary partition table based on the
                // (valid) primary table.
                if (disk.CanWrite)
                {
                    WriteSecondaryHeader();
                }
            }
        }
    }

    private void EstablishReservedPartition(List<GptEntry> allEntries)
    {
        // If no MicrosoftReserved partition, and no Microsoft Data partitions, and the disk
        // has a 'reasonable' size free, create a Microsoft Reserved partition.
        if (CountEntries(allEntries, e => e.PartitionType == GuidPartitionTypes.MicrosoftReserved) == 0
            && CountEntries(allEntries, e => e.PartitionType == GuidPartitionTypes.WindowsBasicData) == 0
            && _diskGeometry.Capacity > 512 * 1024 * 1024)
        {
            var reservedStart = FirstAvailableSector(allEntries);
            var reservedEnd = FindLastFreeSector(reservedStart, allEntries);

            if ((reservedEnd - reservedStart + 1) * _diskGeometry.BytesPerSector > 512 * 1024 * 1024)
            {
                long size = (_diskGeometry.Capacity < 16 * 1024L * 1024 * 1024 ? 32 : 128) * 1024 * 1024;
                reservedEnd = reservedStart + size / _diskGeometry.BytesPerSector - 1;

                var reservedOffset = GetFreeEntryOffset();
                var newReservedEntry = new GptEntry
                {
                    PartitionType = GuidPartitionTypes.MicrosoftReserved,
                    Identity = Guid.NewGuid(),
                    FirstUsedLogicalBlock = reservedStart,
                    LastUsedLogicalBlock = reservedEnd,
                    Attributes = 0,
                    Name = "Microsoft reserved partition"
                };
                newReservedEntry.WriteTo(_entryBuffer.AsSpan(reservedOffset));
                allEntries.Add(newReservedEntry);
            }
        }
    }

    private GptEntry CreateEntry(long startSector, long endSector, Guid type, long attributes, string name)
    {
        if (endSector < startSector)
        {
            throw new ArgumentException("The end sector is before the start sector");
        }

        var offset = GetFreeEntryOffset();
        var newEntry = new GptEntry
        {
            PartitionType = type,
            Identity = Guid.NewGuid(),
            FirstUsedLogicalBlock = startSector,
            LastUsedLogicalBlock = endSector,
            Attributes = (ulong)attributes,
            Name = name
        };
        newEntry.WriteTo(_entryBuffer.AsSpan(offset));

        // Commit changes to disk
        Write();

        return newEntry;
    }

    private long FindGap(long numSectors, long alignmentSectors)
    {
        var list = new List<GptEntry>(GetAllEntries());
        list.Sort();

        var startSector = MathUtilities.RoundUp(_primaryHeader.FirstUsable, alignmentSectors);
        foreach (var entry in list)
        {
            if (
                !Utilities.RangesOverlap(startSector, startSector + numSectors - 1, entry.FirstUsedLogicalBlock,
                    entry.LastUsedLogicalBlock))
            {
                break;
            }

            startSector = MathUtilities.RoundUp(entry.LastUsedLogicalBlock + 1, alignmentSectors);
        }

        if (_diskGeometry.TotalSectorsLong - startSector < numSectors)
        {
            throw new IOException($"Unable to find free space of {numSectors} sectors");
        }

        return startSector;
    }

    private long FirstAvailableSector(List<GptEntry> allEntries)
    {
        var start = _primaryHeader.FirstUsable;

        foreach (var entry in allEntries)
        {
            if (entry.LastUsedLogicalBlock >= start)
            {
                start = entry.LastUsedLogicalBlock + 1;
            }
        }

        return start;
    }

    private long FindLastFreeSector(long start, List<GptEntry> allEntries)
    {
        var end = _primaryHeader.LastUsable;

        foreach (var entry in allEntries)
        {
            if (entry.LastUsedLogicalBlock > start && entry.FirstUsedLogicalBlock <= end)
            {
                end = entry.FirstUsedLogicalBlock - 1;
            }
        }

        return end;
    }

    private void Write()
    {
        WritePrimaryHeader();
        WriteSecondaryHeader();
    }

    private void WritePrimaryHeader()
    {
        var buffer = _diskGeometry.BytesPerSector <= 1024
            ? stackalloc byte[_diskGeometry.BytesPerSector]
            : new byte[_diskGeometry.BytesPerSector];

        _primaryHeader.EntriesCrc = CalcEntriesCrc(_entryBuffer);
        _primaryHeader.WriteTo(buffer);
        _diskData.Position = _diskGeometry.BytesPerSector;
        _diskData.Write(buffer);

        _diskData.Position = 2 * _diskGeometry.BytesPerSector;
        _diskData.Write(_entryBuffer, 0, _entryBuffer.Length);
    }

    private void WriteSecondaryHeader()
    {
        var buffer = _diskGeometry.BytesPerSector <= 1024
            ? stackalloc byte[_diskGeometry.BytesPerSector]
            : new byte[_diskGeometry.BytesPerSector];

        _secondaryHeader.EntriesCrc = CalcEntriesCrc(_entryBuffer);
        _secondaryHeader.WriteTo(buffer);
        _diskData.Position = _diskData.Length - _diskGeometry.BytesPerSector;
        _diskData.Write(buffer);

        _diskData.Position = _secondaryHeader.PartitionEntriesLba * _diskGeometry.BytesPerSector;
        _diskData.Write(_entryBuffer, 0, _entryBuffer.Length);
    }

    private bool ReadEntries(GptHeader header)
    {
        _diskData.Position = header.PartitionEntriesLba * _diskGeometry.BytesPerSector;

        var buffer = _diskData.ReadExactly((int)(header.PartitionEntrySize * header.PartitionEntryCount));

        if (header.EntriesCrc != CalcEntriesCrc(buffer))
        {
            return false;
        }

        _entryBuffer = buffer;
        return true;
    }

    private IEnumerable<GptEntry> GetAllEntries()
    {
        for (var i = 0; i < _primaryHeader.PartitionEntryCount; ++i)
        {
            var entry = new GptEntry();
            entry.ReadFrom(_entryBuffer.AsSpan(i * _primaryHeader.PartitionEntrySize));
            if (entry.PartitionType != Guid.Empty)
            {
                yield return entry;
            }
        }
    }

    private int GetPartitionOffset(int index)
    {
        var found = false;
        var entriesSoFar = 0;
        var position = 0;

        while (!found && position < _primaryHeader.PartitionEntryCount)
        {
            var entry = new GptEntry();
            entry.ReadFrom(_entryBuffer.AsSpan(position * _primaryHeader.PartitionEntrySize));
            if (entry.PartitionType != Guid.Empty)
            {
                if (index == entriesSoFar)
                {
                    found = true;
                    break;
                }

                entriesSoFar++;
            }

            position++;
        }

        if (found)
        {
            return position * _primaryHeader.PartitionEntrySize;
        }

        throw new IOException($"No such partition: {index}");
    }

    private int GetEntryIndex(Guid identity)
    {
        var index = 0;

        for (var i = 0; i < _primaryHeader.PartitionEntryCount; ++i)
        {
            var entry = new GptEntry();
            entry.ReadFrom(_entryBuffer.AsSpan(i * _primaryHeader.PartitionEntrySize));

            if (entry.Identity == identity)
            {
                return index;
            }

            if (entry.PartitionType != Guid.Empty)
            {
                index++;
            }
        }

        throw new IOException("No such partition");
    }

    private int GetFreeEntryOffset()
    {
        for (var i = 0; i < _primaryHeader.PartitionEntryCount; ++i)
        {
            var entry = new GptEntry();
            entry.ReadFrom(_entryBuffer.AsSpan(i * _primaryHeader.PartitionEntrySize));

            if (entry.PartitionType == Guid.Empty)
            {
                return i * _primaryHeader.PartitionEntrySize;
            }
        }

        throw new IOException("No free partition entries available");
    }
}