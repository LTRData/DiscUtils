// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using ExFat.Filesystem;
using LibraryTests.ExFat.Environment;
using System.IO;
using System.Linq;

namespace LibraryTests.ExFat.Tests;
[Trait("Category", "PathFilesystem")]
public class PathFilesystemReadTests
{
    [Fact]
    [Trait("Category", "Read")]
    public void ReadRootFolderEntriesTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var filesystem = new ExFatPathFilesystem(testEnvironment.PartitionStream);
        var entries = filesystem.EnumerateEntries(@$"{Path.DirectorySeparatorChar}").ToArray();
        Assert.Contains(entries, e => e.Path == DiskContent.LongContiguousFileName);
        Assert.Contains(entries, e => e.Path == DiskContent.LongSparseFile1Name);
        Assert.Contains(entries, e => e.Path == DiskContent.EmptyRootFolderFileName);
        Assert.Contains(entries, e => e.Path == DiskContent.LongFolderFileName);
    }

    [Fact]
    [Trait("Category", "Read")]
    public void ReadSubFolderFilesTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var filesystem = new ExFatPathFilesystem(testEnvironment.PartitionStream);
        var entries = filesystem.EnumerateEntries(DiskContent.LongFolderFileName).ToArray();
        Assert.Equal(DiskContent.LongFolderEntriesCount, entries.Length);
    }

    [Fact]
    [Trait("Category", "Read")]
    public void ReadDatesTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var filesystem = new ExFatPathFilesystem(testEnvironment.PartitionStream);
        var c = filesystem.GetCreationTime(DiskContent.LongContiguousFileName);
    }
}