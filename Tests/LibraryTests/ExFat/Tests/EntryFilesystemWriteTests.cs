// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.Streams;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using DiscUtils.ExFat.Internal.Filesystem;
using LibraryTests.ExFat.Environment;

namespace LibraryTests.ExFat.Tests;
[Trait("Category", "EntryFilesystem")]
public class EntryFilesystemWriteTests
{
    [Fact]
    [Trait("Category", "Write")]
    public void AppendTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        var file = filesystem.FindChild(filesystem.RootDirectory, DiskContent.LongContiguousFileName);
        using (var s = filesystem.OpenFile(file, FileAccess.ReadWrite))
        {
            s.Seek(0, SeekOrigin.End);
            s.WriteByte(123);
        }

        Assert.Equal((long)DiskContent.LongFileSize + 1, file.Length);
        using var s2 = filesystem.OpenFile(file, FileAccess.Read);
        s2.Seek(-1, SeekOrigin.End);
        Assert.Equal(123, s2.ReadByte());
        Assert.Equal(-1, s2.ReadByte());
    }

    [Fact]
    [Trait("Category", "Write")]
    public void CreateDirectoryTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        var directoryName = "Orphaaaan 1";
        var s1 = filesystem.CreateDirectory(filesystem.RootDirectory, directoryName);
        var f1 = filesystem.FindChild(filesystem.RootDirectory, directoryName);
        // do more checks?
        Assert.NotNull(f1);
    }

    [Fact]
    [Trait("Category", "Write")]
    public void CreateSubDirectoryTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        var s1 = filesystem.CreateDirectory(filesystem.RootDirectory, "SubOne");
        var n2 = "SubTwo";
        var s2 = filesystem.CreateDirectory(s1, n2);
        var f2 = filesystem.FindChild(s1, n2);
        Assert.NotNull(f2);
    }

    [Fact]
    [Trait("Category", "Write")]
    public void CreateFileTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        using (var s = filesystem.CreateFile(filesystem.RootDirectory, "a.txt"))
        {
            s.WriteByte(65);
        }

        var f = filesystem.FindChild(filesystem.RootDirectory, "a.txt");
        using var s2 = filesystem.OpenFile(f, FileAccess.Read);
        Assert.Equal(65, s2.ReadByte());
        Assert.Equal(-1, s2.ReadByte());
    }

    [Fact]
    [Trait("Category", "Write")]
    public void CreateEmptyFileTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        using (var s = filesystem.CreateFile(filesystem.RootDirectory, "a.txt"))
        {
        }

        var f = filesystem.FindChild(filesystem.RootDirectory, "a.txt");
        using var s2 = filesystem.OpenFile(f, FileAccess.Read);
        Assert.Equal(-1, s2.ReadByte());
    }

    [Fact]
    [Trait("Category", "Write")]
    public void DeleteFileTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        var f = filesystem.FindChild(filesystem.RootDirectory, DiskContent.LongContiguousFileName);
        filesystem.Delete(f);
        Assert.Null(filesystem.FindChild(filesystem.RootDirectory, DiskContent.LongContiguousFileName));
    }

    [Fact]
    [Trait("Category", "Write")]
    public void DeleteDirectoryTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        var f = filesystem.FindChild(filesystem.RootDirectory, DiskContent.EmptyRootFolderFileName);
        filesystem.Delete(f);
        Assert.Null(filesystem.FindChild(filesystem.RootDirectory, DiskContent.EmptyRootFolderFileName));
    }

    [Fact]
    [Trait("Category", "Write")]
    public void TruncateFileTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        var f = filesystem.FindChild(filesystem.RootDirectory, DiskContent.LongContiguousFileName);
        using var s = filesystem.OpenFile(f, FileAccess.ReadWrite);
        s.SetLength(16);
        s.Seek(0, SeekOrigin.Begin);
        var b = new byte[8];
        Assert.Equal(8, s.Read(b, 0, b.Length));
        Assert.Equal(DiskContent.GetLongContiguousFileNameOffsetValue(0), EndianUtilities.ToUInt64LittleEndian(b));
        Assert.Equal(8, s.Read(b, 0, b.Length));
        Assert.Equal(DiskContent.GetLongContiguousFileNameOffsetValue(8), EndianUtilities.ToUInt64LittleEndian(b));
        Assert.Equal(0, s.Read(b, 0, b.Length));
    }

    [Fact]
    [Trait("Category", "Write")]
    public void LengthenFileTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        using var s = filesystem.CreateFile(filesystem.RootDirectory, "newlong");
        var length = 200000;
        s.SetLength(length);
        s.Seek(-1, SeekOrigin.End);
        s.WriteByte(12);
        Assert.Equal(length, s.Length);
        s.WriteByte(34);
        Assert.Equal(length + 1, s.Length);
    }

    [Fact]
    [Trait("Category", "Write")]
    public void MoveFileTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        var aFile = filesystem.FindChild(filesystem.RootDirectory, DiskContent.LongContiguousFileName);
        var aFolder = filesystem.FindChild(filesystem.RootDirectory, DiskContent.EmptyRootFolderFileName);
        filesystem.Move(aFile, aFolder, "noob");
    }

    [Fact]
    [Trait("Category", "Write")]
    public void RandomReadWriteTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx(true);
        using var filesystem = new ExFatEntryFilesystem(testEnvironment.PartitionStream);
        var testFolder = filesystem.CreateDirectory(filesystem.RootDirectory, "inner looping test");
        var random = new Random(0);
        var fileIndex = 0;
        var catalogCache = new List<string>();
        for (var loop = 0; loop < 10; loop++)
        {
            // delete some
            var existingFiles = filesystem.EnumerateFileSystemEntries(testFolder).ToArray();
            for (var index = 0; index < existingFiles.Length; index++)
            {
                if (random.Next(0, 2) == 0)
                {
                    filesystem.Delete(existingFiles[index]);
                    Assert.True(catalogCache.Remove(existingFiles[index].Name));
                }
            }
            // add some
            var newFilesCount = random.Next(100, 300);
            for (var index = 0; index < newFilesCount; index++)
            {
                ++fileIndex;
                var fileName = fileIndex.ToString();
                using (var s = filesystem.CreateFile(testFolder, fileName))
                {
                    var l = random.Next(0, 1000);
                    var b = BitConverter.GetBytes(fileIndex);
                    for (var i = 0; i < l; i++)
                    {
                        s.Write(b, 0, b.Length);
                    }
                }

                catalogCache.Add(fileName);
            }
        }
    }

    [Fact]
    [Trait("Category", "Write")]
    public void NewPartitionTest()
    {
        using (EntryFilesystemTestEnvironment.FromNewVhdx(true))
        {
        }
    }

    [Fact]
    [Trait("Category", "Write")]
    public void CreateFolderOnNewPartitionTest()
    {
        using var testEnvironment = EntryFilesystemTestEnvironment.FromNewVhdx(true);
        var filesystem = testEnvironment.FileSystem;
        filesystem.CreateDirectory(filesystem.RootDirectory, "folder");
        var folder = filesystem.FindChild(filesystem.RootDirectory, "folder");
        Assert.NotNull(folder);
    }
}