// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.IO;
using DiscUtils.ExFat.Internal.Partition.Entries;
using DiscUtils.ExFat.Internal.IO;

namespace DiscUtils.ExFat.Internal.Partition;
partial class ExFatPartition
{
    /// <summary>
    /// Formats the specified partition stream.
    /// </summary>
    /// <param name="partitionStream">The partition stream.</param>
    /// <param name="options">The options.</param>
    /// <param name="volumeLabel">The volume label.</param>
    /// <returns></returns>
    public static ExFatPartition Format(Stream partitionStream, ExFatFormatOptions options, string volumeLabel = null)
    {
        var partition = new ExFatPartition(partitionStream, 0, false);
        partitionStream.Seek(0, SeekOrigin.Begin);

        var volumeSpace = options?.VolumeSpace ?? (ulong)partitionStream.Length;
        var bytesPerSector = options?.BytesPerSector ?? 512;
        var totalSectors = volumeSpace / bytesPerSector;
        var sectorsPerCluster = options?.SectorsPerCluster ?? ComputeSectorsPerCluster(totalSectors);
        if (sectorsPerCluster > 1 << 25)
        {
            throw new ArgumentException("Sectors per cluster can not exceed 2^25");
        }

        const uint fats = 1;
        const uint usedFats = fats;
        const uint bootSectors = 12;
        const uint bpbSectors = 2 * bootSectors;

        // create bootsector
        var bootSectorBytes = new byte[bytesPerSector * 12];
        var bootSector = new ExFatBootSector(bootSectorBytes);
        var totalClusters = (uint)(totalSectors - bpbSectors) / (usedFats * 4 / bytesPerSector + sectorsPerCluster);
        var sectorsPerFat = totalClusters * 4 / bytesPerSector;
        bootSector.JmpBoot.Set(ExFatBootSector.DefaultJmpBoot);
        bootSector.OemName.Value = ExFatBootSector.ExFatOemName;
        bootSector.VolumeLengthSectors.Value = totalSectors;
        bootSector.FatOffsetSector.Value = Align(bpbSectors, bytesPerSector);
        bootSector.FatLengthSectors.Value = Align(sectorsPerFat, bytesPerSector);
        bootSector.ClusterOffsetSector.Value = Align(bootSector.FatOffsetSector.Value + usedFats * bootSector.FatLengthSectors.Value, bytesPerSector);
        totalClusters = (uint)((volumeSpace / bytesPerSector - bootSector.ClusterOffsetSector.Value) / sectorsPerCluster);
        if (totalClusters > 0xFFFFFFF0)
        {
            throw new ArgumentException("clusters are too small to address full disk");
        }

        bootSector.ClusterCount.Value = totalClusters;
        bootSector.VolumeSerialNumber.Value = (uint)new Random().Next();
        bootSector.FileSystemRevision.Value = 256;
        bootSector.VolumeFlags.Value = 0;
        bootSector.BytesPerSector.Value = bytesPerSector;
        bootSector.SectorsPerCluster.Value = sectorsPerCluster;
        bootSector.NumberOfFats.Value = (byte)fats;
        bootSector.DriveSelect.Value = 0x80;
        bootSector.PercentInUse.Value = 0xFF;
        for (var sectorIndex = 0; sectorIndex <= 8; sectorIndex++)
        {
            bootSectorBytes[sectorIndex * bytesPerSector + bytesPerSector - 2] = 0x55;
            bootSectorBytes[sectorIndex * bytesPerSector + bytesPerSector - 1] = 0xAA;
        }

        partition.BootSector = bootSector;

        // prepare FAT
        partition.ClearFat();
        partition.SetNextCluster(0, Cluster.Marker);
        partition.SetNextCluster(1, Cluster.Last);

        // create allocation bitmap
        var allocationBitmapEntry = CreateAllocationBitmap(partition, totalClusters);

        // create root directory
        var directoryDataDescriptor = new DataDescriptor(0, false, ulong.MaxValue, ulong.MaxValue);
        using (var s = partition.OpenClusterStream(directoryDataDescriptor, FileAccess.ReadWrite,
            d => directoryDataDescriptor = new DataDescriptor(d.FirstCluster, d.Contiguous, long.MaxValue, long.MaxValue)))
        {
            s.SetDataLength(bytesPerSector * sectorsPerCluster);
        }

        bootSector.RootDirectoryCluster.Value = directoryDataDescriptor.FirstCluster.ToUInt32();

        // boot sector is now complete
        Span<byte> checksum = stackalloc byte[sizeof(uint)];
        bootSector.ComputeChecksum(checksum);
        for (var offset = 11 * bytesPerSector; offset < 12 * bytesPerSector; offset += 4)
        {
            bootSectorBytes[offset] = checksum[0];
            bootSectorBytes[offset + 1] = checksum[1];
            bootSectorBytes[offset + 2] = checksum[2];
            bootSectorBytes[offset + 3] = checksum[3];
        }

        partition.WriteSectors(0, bootSectorBytes, (int)bootSectors);
        partition.WriteSectors(bootSectors, bootSectorBytes, (int)bootSectors);

        using (var directoryStream = partition.OpenClusterStream(directoryDataDescriptor, FileAccess.ReadWrite))
        {
            allocationBitmapEntry.Write(directoryStream);

            CreateUpCaseTable(partition, directoryStream);
            CreateVolumeLabel(directoryStream, volumeLabel);
        }

        partition.Flush();
        return partition;
    }

    private static uint ComputeSectorsPerCluster(ulong totalSectors)
    {
        // this is based on the following defaults:
        // up to 256 MB -> 4 kB clusters
        // up to 32 GB -> 32 kB clusters
        // up to 256 TB -> 128 kB clusters
        // Also, remember 1 KB = 1<<10, 1 GB = 1<<20, 1 TB = 1<<30
        const int refSectorBits = 9; // 512 B
        if (totalSectors <= 256 << (20 - refSectorBits))
        {
            return 4 << (10 - refSectorBits);
        }

        if (totalSectors <= 32 << (30 - refSectorBits))
        {
            return 32 << (10 - refSectorBits);
        }

        if (totalSectors <= 256 << (30 - refSectorBits))
        {
            return 128 << (10 - refSectorBits);
        }

        throw new ArgumentException("Sectors per cluster value has to be provided");
    }

    private static uint Align(uint value, uint bytesPerSector)
    {
        const int alignment = 4 << 10; // 4K alignmet
        if (bytesPerSector > alignment)
        {
            return value;
        }

        var r = alignment / bytesPerSector - 1;
        return (value + r) & ~r;
    }

    private static void CreateVolumeLabel(ClusterStream directoryStream, string volumeLabel)
    {
        if (volumeLabel == null)
        {
            return;
        }

        var volumeLabelEntry = new VolumeLabelExFatDirectoryEntry(new(new byte[32]));
        volumeLabelEntry.EntryType.Value = ExFatDirectoryEntryType.VolumeLabel | ExFatDirectoryEntryType.InUse;
        volumeLabelEntry.VolumeLabel = volumeLabel;
        volumeLabelEntry.Write(directoryStream);
    }

    private static AllocationBitmapExFatDirectoryEntry CreateAllocationBitmap(ExFatPartition partition, uint totalClusters)
    {
        var allocationBitmap = new ExFatAllocationBitmap();
        allocationBitmap.Open(null, 2, totalClusters, true);
        partition._allocationBitmap = allocationBitmap;

        var dataDescriptor = new DataDescriptor(0, false, 0, 0);
        using (var allocationBitmapStream = partition.OpenClusterStream(dataDescriptor, FileAccess.ReadWrite, d => dataDescriptor = d))
        {
            allocationBitmap.Write(allocationBitmapStream);
        }

        var allocationBitmapEntry = new AllocationBitmapExFatDirectoryEntry(new(new byte[32]));
        allocationBitmapEntry.EntryType.Value = ExFatDirectoryEntryType.AllocationBitmap | ExFatDirectoryEntryType.InUse;
        allocationBitmapEntry.BitmapFlags.Value = 0;
        allocationBitmapEntry.FirstCluster.Value = (uint)dataDescriptor.FirstCluster.Value;
        allocationBitmapEntry.DataLength.Value = (totalClusters + 7) / 8;
        return allocationBitmapEntry;
    }

    private static void CreateUpCaseTable(ExFatPartition partition, Stream directoryStream)
    {
        var upCaseTable = new ExFatUpCaseTable();
        upCaseTable.SetDefault();
        partition._upCaseTable = upCaseTable;
        var upCaseTableDataDescriptor = new DataDescriptor(0, false, long.MaxValue, long.MaxValue);
        long length;
        uint checksum;
        using (var upCaseStream = partition.OpenClusterStream(upCaseTableDataDescriptor, FileAccess.ReadWrite, d => upCaseTableDataDescriptor = d))
        {
            checksum = upCaseTable.Write(upCaseStream);
            length = upCaseStream.Position;
        }

        var upCaseTableEntry = new UpCaseTableExFatDirectoryEntry(new(new byte[32]));
        upCaseTableEntry.EntryType.Value = ExFatDirectoryEntryType.UpCaseTable | ExFatDirectoryEntryType.InUse;
        upCaseTableEntry.TableChecksum.Value = checksum;
        upCaseTableEntry.FirstCluster.Value = (uint)upCaseTableDataDescriptor.FirstCluster.Value;
        upCaseTableEntry.DataLength.Value = (ulong)length;
        upCaseTableEntry.Write(directoryStream);
    }
}
