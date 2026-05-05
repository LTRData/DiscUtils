// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.ExFat;
using LibraryTests.ExFat.Environment;
using LTRData.Extensions.Buffers;
using System.IO;
using System.Linq;

namespace LibraryTests.ExFat.Tests;

[Trait("Category", "DiscUtils")]
public class DiscFilesystemTests
{
    [Fact]
    [Trait("Category", "Read")]
    public void ReadAllFiles()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var filesystem = new ExFatFileSystem(testEnvironment.PartitionStream);
        var allFiles = filesystem.GetFiles("", "0*", SearchOption.AllDirectories);
        Assert.True(allFiles.All(p => Path.GetFileName(p).StartsWith('0')));
    }

    [Fact]
    [Trait("Category", "Read")]
    public void ReadRootFiles()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var filesystem = new ExFatFileSystem(testEnvironment.PartitionStream);
        var allFiles = filesystem.GetFiles("");
        Assert.Contains(DiskContent.LongContiguousFileName, allFiles);
        Assert.Contains(DiskContent.LongSparseFile1Name, allFiles);
        Assert.Contains(DiskContent.LongSparseFile2Name, allFiles);
        Assert.DoesNotContain(DiskContent.EmptyRootFolderFileName, allFiles);
        Assert.DoesNotContain(DiskContent.LongFolderFileName, allFiles);
    }

    [Fact]
    [Trait("Category", "Read")]
    public void ReadRootDirectories()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var filesystem = new ExFatFileSystem(testEnvironment.PartitionStream);
        var allDirectories = filesystem.GetDirectories("");
        Assert.DoesNotContain(DiskContent.LongContiguousFileName, allDirectories);
        Assert.DoesNotContain(DiskContent.LongSparseFile1Name, allDirectories);
        Assert.DoesNotContain(DiskContent.LongSparseFile2Name, allDirectories);
        Assert.Contains(DiskContent.EmptyRootFolderFileName, allDirectories);
        Assert.Contains(DiskContent.LongFolderFileName, allDirectories);
    }

    [Fact]
    [Trait("Category", "Write")]
    public void MoveFile()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var filesystem = new ExFatFileSystem(testEnvironment.PartitionStream);
        using (var a = filesystem.OpenFile("a", FileMode.Create))
        {
            a.WriteByte(1);
        }

        Assert.True(filesystem.FileExists("a"));
        filesystem.MoveFile("a", "b");
        Assert.False(filesystem.FileExists("a"));
        Assert.True(filesystem.FileExists("b"));
    }

    [Fact]
    [Trait("Category", "Write")]
    public void MoveFileToDirectory()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var filesystem = new ExFatFileSystem(testEnvironment.PartitionStream);
        using (var a = filesystem.OpenFile("a", FileMode.Create))
        {
            a.WriteByte(1);
        }

        filesystem.CreateDirectory("d");
        Assert.True(filesystem.FileExists("a"));
        filesystem.MoveFile("a", "d");
        Assert.False(filesystem.FileExists("a"));
        Assert.True(filesystem.FileExists(Path.Combine("d", "a")));
    }
}