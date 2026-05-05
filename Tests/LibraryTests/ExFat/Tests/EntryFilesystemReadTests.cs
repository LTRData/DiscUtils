// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.Streams;
using DiscUtils.ExFat.Internal.Filesystem;
using LibraryTests.ExFat.Environment;
using System.IO;

namespace LibraryTests.ExFat.Tests;

[Trait("Category", "EntryFilesystem")]
public class EntryFilesystemReadTests
{
    [Fact]
    [Trait("Category", "Read")]
    public void FindFile()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        var file = filesystem.FindChild(filesystem.RootDirectory, DiskContent.LongContiguousFileName);
        Assert.NotNull(file);
    }

    [Fact]
    [Trait("Category", "Read")]
    public void ReadFile()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        var file = filesystem.FindChild(filesystem.RootDirectory, DiskContent.LongContiguousFileName);
        var vb = new byte[sizeof(ulong)];
        using var stream = filesystem.OpenFile(file, FileAccess.Read);
        for (ulong offset = 0; offset < DiskContent.LongFileSize; offset += 8)
        {
            stream.ReadExactly(vb, 0, vb.Length);
            var v = EndianUtilities.ToUInt64LittleEndian(vb);
            Assert.Equal(v, DiskContent.GetLongContiguousFileNameOffsetValue(offset));
        }
    }

    [Fact]
    [Trait("Category", "Read")]
    public void UpdateLastAccessTime()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        var file = filesystem.FindChild(filesystem.RootDirectory, DiskContent.LongContiguousFileName);
        var access0 = file.LastAccessTime;
        using (var stream = filesystem.OpenFile(file, FileAccess.Read))
        {
        }

        var access1 = file.LastAccessTime;
        Assert.True(access1 > access0);
        var file2 = filesystem.FindChild(filesystem.RootDirectory, DiskContent.LongContiguousFileName);
        var access2 = file2.LastAccessTime;
        Assert.Equal(access1, access2);
    }
}