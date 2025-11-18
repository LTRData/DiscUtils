// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.IO;
using System.Linq;
using DiscUtils.ExFat;
using DiscUtils;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using DiscUtils.Vhdx;
using System.Runtime.Versioning;
using DiscUtils.Streams.Compatibility;
using LibraryTests.ExFat;

namespace ExFat.Generator;
public static class Program
{
    public static void Main(params string[] _)
    {
        using var diskStream = File.OpenRead("D:\\rozina-pascal.localcopy.vhdx");
        var disk = new Disk(diskStream, Ownership.Dispose);
        var volume = VolumeManager.GetPhysicalVolumes(disk).First();
        var volumeStream = volume.Open();
        using var fs = new ExFatFileSystem(volumeStream);
        var f = fs.FileExists(@"rozina-pascal\storage\parameters");
        var d = fs.DirectoryExists(@"rozina-pascal\storage\parameters");
    }

    public static void Main4(string[] _)
    {
        var label = "Zap!";
        var capacity = 2L << 40;
        var blockSize = 4 << 20;
        using var diskStream = File.Create("big.vhdx");
        using var disk = Disk.InitializeDynamic(diskStream, Ownership.Dispose, capacity, Geometry.FromCapacity(blockSize));
        var gpt = GuidPartitionTable.Initialize(disk);
        gpt.Create(gpt.FirstUsableSector, gpt.LastUsableSector, GuidPartitionTypes.WindowsBasicData, 0, null);
        var volume = VolumeManager.GetPhysicalVolumes(disk).First();
        var bytesPerSector = (uint)(volume.PhysicalGeometry?.BytesPerSector ?? 512);
        var clusterCount = 1 << 25;// uint.MaxValue - 16;
        var clusterSize = capacity / clusterCount;
        var clusterBits = (int)Math.Ceiling(Math.Log(clusterSize) / Math.Log(2));
        if (clusterBits > 18)
        {
            clusterBits = 18;
        }
        //clusterBits = 20;
        using var fs = ExFatFileSystem.Format(volume, new ExFatFormatOptions { SectorsPerCluster = (1u << clusterBits) / bytesPerSector }, label: label);
    }

    public static void Main2(string[] _)
    {
        File.Copy("Empty1.vhdx", "Empty.vhdx", true);
        using (var disk = new Disk("Empty.vhdx"))
        {
            //var gpt = GuidPartitionTable.Initialize(disk);
            //gpt.Create(gpt.FirstUsableSector, gpt.LastUsableSector, GuidPartitionTypes.WindowsBasicData, 0, null);
            var volume = VolumeManager.GetPhysicalVolumes(disk)[1];
            using var fs = ExFatFileSystem.Format(volume);
            fs.CreateDirectory("a folder");
        }

        using (var disk = new Disk("Empty.vhdx"))
        {
            var volume = VolumeManager.GetPhysicalVolumes(disk)[1];
            using var fs2 = new ExFatFileSystem(volume.Open());
            var i = fs2.GetDirectoryInfo("a folder");
            var e = fs2.GetDirectories("");
        }
    }

#if NETSTANDARD || NETCOREAPP
    [SupportedOSPlatform("windows")]
#endif
    public static void Main111(string[] args)
    {
        const string drive = "X:";

        // label
        var driveInfo = new DriveInfo(drive)
        {
            VolumeLabel = DiskContent.VolumeLabel
        };

        Span<byte> b1 = stackalloc byte[sizeof(ulong)];

        // long contiguous file
        using (var fc = File.Create(Path.Combine(drive, DiskContent.LongContiguousFileName)))
        {
            for (ulong offset = 0; offset < DiskContent.LongFileSize; offset += sizeof(ulong))
            {
                EndianUtilities.WriteBytesLittleEndian(DiskContent.GetLongContiguousFileNameOffsetValue(offset), b1);
                fc.Write(b1);
            }
        }

        Span<byte> b2 = stackalloc byte[sizeof(ulong)];

        // long sparse files
        const uint chunks = 1u << 10;
        for (ulong offsetBase = 0; offsetBase < DiskContent.LongFileSize; offsetBase += chunks)
        {
            using var fs1 = File.OpenWrite(Path.Combine(drive, DiskContent.LongSparseFile1Name));
            using var fs2 = File.OpenWrite(Path.Combine(drive, DiskContent.LongSparseFile2Name));
            fs1.Seek(0, SeekOrigin.End);
            fs2.Seek(0, SeekOrigin.End);
            for (ulong subOffset = 0; subOffset < chunks; subOffset += sizeof(ulong))
            {
                var offset = offsetBase + subOffset;
                EndianUtilities.WriteBytesLittleEndian(DiskContent.GetLongSparseFile1NameOffsetValue(offset), b1);
                fs1.Write(b1);
                EndianUtilities.WriteBytesLittleEndian(DiskContent.GetLongSparseFile2NameOffsetValue(offset), b2);
                fs2.Write(b2);
            }
        }

        // An empty folder
        Directory.CreateDirectory(Path.Combine(drive, DiskContent.EmptyRootFolderFileName));

        // A folder full of garbage
        var longDirectoryPath = Path.Combine(drive, DiskContent.LongFolderFileName);
        Directory.CreateDirectory(longDirectoryPath);
        for (var subFileIndex = 0; subFileIndex < DiskContent.LongFolderEntriesCount; subFileIndex++)
        {
            var path = Path.Combine(longDirectoryPath, Guid.NewGuid().ToString("N"));
            using var t = File.CreateText(path);
            t.WriteLine(subFileIndex);
        }
    }
}
