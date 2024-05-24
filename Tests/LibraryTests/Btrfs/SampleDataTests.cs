using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DiscUtils;
using DiscUtils.Btrfs;
using DiscUtils.Streams;
using DiscUtils.Vhdx;
using LibraryTests.Utilities;
using LTRData.Extensions.Formatting;
using Xunit;
using static LibraryTests.Helpers.Helpers;

namespace LibraryTests.Btrfs;

public class SampleDataTests
{
    [Fact]
    public void BtrfsVhdxZip()
    {
        DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(Disk).GetTypeInfo().Assembly);
        DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(BtrfsFileSystem).GetTypeInfo().Assembly);
        using var vhdx = LoadTestDataFileFromGZipFile("Btrfs", "btrfs.vhdx.gz");
        using var diskImage = new DiskImageFile(vhdx, Ownership.Dispose);
        using var disk = new Disk(new List<DiskImageFile> { diskImage }, Ownership.Dispose);
        var manager = new VolumeManager(disk);
        var logicalVolumes = manager.GetLogicalVolumes();
        Assert.Single(logicalVolumes);

        var volume = logicalVolumes[0];
        var filesystems = FileSystemManager.DetectFileSystems(volume);
        Assert.Single(filesystems);

        var filesystem = filesystems[0];
        Assert.Equal("btrfs", filesystem.Name);

        using (var btrfs = filesystem.Open(volume))
        {
            Assert.IsType<BtrfsFileSystem>(btrfs);

            Assert.Equal(1072594944, btrfs.AvailableSpace);
            Assert.Equal(1072693248, btrfs.Size);
            Assert.Equal(98304, btrfs.UsedSpace);

            var subvolumes = ((BtrfsFileSystem)btrfs).GetSubvolumes().ToArray();
            Assert.Single(subvolumes);
            Assert.Equal(256UL, subvolumes[0].Id);
            Assert.Equal("subvolume", subvolumes[0].Name);

            Assert.Equal("text\n", GetFileContent(Path.Combine("folder","subfolder", "file"), btrfs));
            Assert.Equal("f64464c2024778f347277de6fa26fe87", GetFileChecksum(Path.Combine("folder", "subfolder", "f64464c2024778f347277de6fa26fe87"), btrfs));
            Assert.Equal("fa121c8b73cf3b01a4840b1041b35e9f", GetFileChecksum(Path.Combine("folder", "subfolder", "fa121c8b73cf3b01a4840b1041b35e9f"), btrfs));
            AssertAllZero(Path.Combine("folder", "subfolder", "sparse"), btrfs);
            Assert.Equal("test\n", GetFileContent(Path.Combine("subvolume", "subvolumefolder", "subvolumefile"), btrfs));
            Assert.Equal("b0d5fae237588b6641f974459404d197", GetFileChecksum(Path.Combine("folder", "subfolder", "compressed"), btrfs));
            Assert.Equal("test\n", GetFileContent(Path.Combine("folder", "symlink"), btrfs)); //PR#36
            Assert.Equal("b0d5fae237588b6641f974459404d197", GetFileChecksum(Path.Combine("folder", "subfolder", "lzo"), btrfs));
        }

        using var subvolume = new BtrfsFileSystem(volume.Open(), new BtrfsFileSystemOptions { SubvolumeId = 256, VerifyChecksums = true });
        Assert.Equal("test\n", GetFileContent(Path.Combine("subvolumefolder", "subvolumefile"), subvolume));
    }
}