using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DiscUtils;
using DiscUtils.Complete;
using DiscUtils.Streams;
using DiscUtils.Xfs;
using DiscUtils.Vhdx;
using LibraryTests.Utilities;
using Xunit;
using File=System.IO.File;
using static LibraryTests.Helpers.Helpers;

namespace LibraryTests.Xfs;

public class SampleDataTests
{
    [Fact]
    public void XfsVhdxZip()
    {
        SetupHelper.SetupComplete();
        using var vhdx = Helpers.Helpers.LoadTestDataFileFromGZipFile("Xfs", "xfs.vhdx.gz");
        using var diskImage = new DiskImageFile(vhdx, Ownership.Dispose);
        using var disk = new Disk(new List<DiskImageFile> { diskImage }, Ownership.Dispose);
        var manager = new VolumeManager(disk);
        var logicalVolumes = manager.GetLogicalVolumes();
        Assert.Single(logicalVolumes);

        var volume = logicalVolumes[0];
        var filesystems = FileSystemManager.DetectFileSystems(volume);
        Assert.Single(filesystems);

        var filesystem = filesystems[0];
        Assert.Equal("xfs", filesystem.Name);

        using var xfs = filesystem.Open(volume);
        Assert.IsType<XfsFileSystem>(xfs);

        Assert.Equal(9081139200, xfs.AvailableSpace);
        Assert.Equal(10725863424, xfs.Size);
        Assert.Equal(1644724224, xfs.UsedSpace);
        ValidateContent(xfs);
    }

    [Fact]
    public void Xfs5VhdxZip()
    {
        SetupHelper.SetupComplete();
        using var vhdx = Helpers.Helpers.LoadTestDataFileFromGZipFile("Xfs", "xfs5.vhdx.gz");
        using var diskImage = new DiskImageFile(vhdx, Ownership.Dispose);
        using var disk = new Disk(new List<DiskImageFile> { diskImage }, Ownership.Dispose);
        var manager = new VolumeManager(disk);
        var logicalVolumes = manager.GetLogicalVolumes();
        Assert.Single(logicalVolumes);

        var volume = logicalVolumes[0];
        var filesystems = FileSystemManager.DetectFileSystems(volume);
        Assert.Single(filesystems);

        var filesystem = filesystems[0];
        Assert.Equal("xfs", filesystem.Name);

        using var xfs = filesystem.Open(volume);
        Assert.IsType<XfsFileSystem>(xfs);

        Assert.Equal(9080827904, xfs.AvailableSpace);
        Assert.Equal(10725883904, xfs.Size);
        Assert.Equal(1645056000, xfs.UsedSpace);
        ValidateContent(xfs);
    }

    private static void ValidateContent(DiscFileSystem xfs)
    {
        Assert.True(xfs.DirectoryExists(""));
        Assert.True(xfs.FileExists(Path.Combine("folder", "nested", "file")));
        Assert.Empty(xfs.GetFileSystemEntries("empty"));
        for (var i = 1; i <= 1000; i++)
        {
            Assert.True(xfs.FileExists(Path.Combine("folder", $"file.{i}")), $"File file.{i} not found");
        }

        var md5 = GetFileChecksum(Path.Combine("folder", "file.100"), xfs);
        Assert.Equal("620f0b67a91f7f74151bc5be745b7110", md5);

        md5 = GetFileChecksum(Path.Combine("folder", "file.random"), xfs);
        Assert.Equal("9a202a11d6e87688591eb97714ed56f1", md5);

        for (var i = 1; i <= 999; i++)
        {
            Assert.True(xfs.FileExists(Path.Combine("huge", $"{i}")), $"File huge/{i} not found");
        }
    }
}