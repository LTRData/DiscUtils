// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using ExFat.Filesystem;
using LibraryTests.ExFat.Environment;
using System;
using System.IO;

namespace LibraryTests.ExFat.Tests;
[Trait("Category", "PathFilesystem")]
public class PathFilesystemWriteTests
{
    private static bool IsAlmostMoreRecentThan(DateTime test, DateTime reference)
    {
        var dt = test - reference;
        // for some unknown (and strange) reason, a strict comparison fails on AppVeyor.
        // I'd love to see how they manage the time
        return dt.TotalHours > -1;
    }

    [Fact]
    [Trait("Category", "Write")]
    public void CreateDirectory()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatPathFilesystem(testEnvironment.PartitionStream);
        var now = DateTime.UtcNow;
        var path = @"zzzz";
        filesystem.CreateDirectory(path);
        var d = filesystem.GetCreationTimeUtc(path);
        Assert.True(IsAlmostMoreRecentThan(d, now));
    }

    [Fact]
    [Trait("Category", "Write")]
    public void CreateDirectoryTree()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatPathFilesystem(testEnvironment.PartitionStream);
        var now = DateTime.UtcNow;
        var path = Path.Combine("a", "b", "c");
        filesystem.CreateDirectory(path);
        var d = filesystem.GetCreationTimeUtc(path);
        Assert.True(IsAlmostMoreRecentThan(d, now));
    }

    [Fact]
    [Trait("Category", "Write")]
    public void DeleteTree()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatPathFilesystem(testEnvironment.PartitionStream);
        filesystem.DeleteTree(DiskContent.LongFolderFileName);
        Assert.DoesNotContain(filesystem.EnumerateEntries(""), e => e.Path == $@"{Path.DirectorySeparatorChar}{DiskContent.LongFolderFileName}");
    }

    [Fact]
    [Trait("Category", "Write")]
    public void CreateFileTree()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatPathFilesystem(testEnvironment.PartitionStream);
        filesystem.CreateDirectory("a");
        using (var s = filesystem.Open(@$"a{Path.DirectorySeparatorChar}b.txt", FileMode.Create, FileAccess.ReadWrite))
        {
            s.WriteByte(66);
        }

        using var r = filesystem.Open(@$"a{Path.DirectorySeparatorChar}b.txt", FileMode.Open, FileAccess.Read);
        Assert.Equal(66, r.ReadByte());
        Assert.Equal(-1, r.ReadByte());
    }

    [Fact]
    [Trait("Category", "Write")]
    public void MoveTree()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatPathFilesystem(testEnvironment.PartitionStream);
        filesystem.Move(DiskContent.LongSparseFile1Name, DiskContent.EmptyRootFolderFileName);
        Assert.Null(filesystem.GetInformation(DiskContent.LongSparseFile1Name));
        Assert.NotNull(
            filesystem.GetInformation(DiskContent.EmptyRootFolderFileName + Path.DirectorySeparatorChar +
                                      DiskContent.LongSparseFile1Name));
    }
}