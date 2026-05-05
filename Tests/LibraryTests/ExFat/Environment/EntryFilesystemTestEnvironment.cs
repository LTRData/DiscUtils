// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.Partitions;
using DiscUtils.Streams;
using DiscUtils.Vhdx;
using System.IO;
using System.Linq;
using System;
using DiscUtils.ExFat.Internal.Filesystem;
using DiscUtils.ExFat.Internal;
using DiscUtils;

namespace LibraryTests.ExFat.Environment;

internal class EntryFilesystemTestEnvironment : TestEnvironment
{
    public ExFatEntryFilesystem FileSystem { get; private set; } = null!;

    public static EntryFilesystemTestEnvironment FromNewVhdx(bool allowKeepDebug = false, long length = 10L << 30)
    {
        var testEnvironment = new EntryFilesystemTestEnvironment();
        testEnvironment.CreateVhdx(allowKeepDebug, length);
        return testEnvironment;
    }

    public override void Dispose()
    {
        FileSystem.Dispose();
        base.Dispose();
    }

    private void CreateVhdx(bool allowKeepDebug, long length)
    {
        var diskStream = CreateVhdxStream(allowKeepDebug);
        disk = Disk.InitializeDynamic(diskStream, Ownership.Dispose, length, Geometry.FromCapacity(128 << 20));
        var gpt = GuidPartitionTable.Initialize(disk);
        gpt.Create(gpt.FirstUsableSector, gpt.LastUsableSector, GuidPartitionTypes.WindowsBasicData, 0, null);
        var volume = VolumeManager.GetPhysicalVolumes(disk).First();
        volumeId = volume.PartitionIdentity;
        var bytesPerSector = (uint)(volume.PhysicalGeometry?.BytesPerSector ?? 512);
        var clusterCount = 1 << 25;
        var clusterSize = length / clusterCount;
        var clusterBits = (int)Math.Ceiling(Math.Log(clusterSize) / Math.Log(2));
        if (clusterBits > 18)
        {
            clusterBits = 18;
        }
        else if (clusterBits < 11)
        {
            clusterBits = 11;
        }

        FileSystem = ExFatEntryFilesystem.Format(volume.Open(),
            new ExFatFormatOptions { SectorsPerCluster = (1u << clusterBits) / bytesPerSector });
    }

    private Stream CreateVhdxStream(bool allowKeepDebug)
    {
        if (!allowKeepDebug)
        {
            return new MemoryStream();
        }

        vhdxPath = Path.Combine(Path.GetTempPath(), $"exFAT test (to be removed) {Guid.NewGuid():N}.vhdx");
        return File.Create(vhdxPath, 1 << 20);
    }
}