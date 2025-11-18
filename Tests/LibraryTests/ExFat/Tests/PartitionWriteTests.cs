// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.Streams;
using ExFat.IO;
using ExFat.Partition;
using LibraryTests.ExFat.Environment;
using System;
using System.IO;
using System.Linq;

namespace LibraryTests.ExFat.Tests;
[Trait("Category", "Partition")]
public class PartitionWriteTests
{
    private static void OverwriteTest(ExFatPartition partition, string fileName, Func<ulong, ulong> getOffsetValue)
    {
        var fileEntry = partition.GetMetaEntries(partition.RootDirectoryDataDescriptor)
            .Single(e => e.ExtensionsFileName == fileName);
        var buffer = new byte[8];
        var dataDescriptor = fileEntry.DataDescriptor;
        using (var overwrite = partition.OpenDataStream(dataDescriptor, FileAccess.ReadWrite))
        {
            for (ulong offset = 0; offset < 2 * DiskContent.LongFileSize; offset += 8)
            {
                EndianUtilities.WriteBytesLittleEndian(getOffsetValue(offset), buffer);
                overwrite.Write(buffer, 0, 8);
            }
        }

        Assert.NotNull(dataDescriptor);

        using var read = partition.OpenDataStream(
            new DataDescriptor(dataDescriptor.Value.FirstCluster, false, DiskContent.LongFileSize * 2,
                DiskContent.LongFileSize * 2), FileAccess.Read);
        for (ulong offset = 0; offset < 2 * DiskContent.LongFileSize; offset += 8)
        {
            var bytesRead = read.Read(buffer, 0, buffer.Length);
            Assert.Equal(bytesRead, buffer.Length);
            var readValue = EndianUtilities.ToUInt64LittleEndian(buffer);
            var expectedValue = getOffsetValue(offset);
            Assert.Equal(expectedValue, readValue);
        }

        Assert.Equal(0, read.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    [Trait("Category", "Overwrite")]
    [Trait("Category", "Write")]
    public void OverwriteSparseTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var partition = new ExFatPartition(testEnvironment.PartitionStream);
        OverwriteTest(partition, DiskContent.LongSparseFile1Name, offset => offset / 7);
        // now check nothing was overwritten
        PartitionReadTests.ReadFile(partition, DiskContent.LongSparseFile2Name,
            DiskContent.GetLongSparseFile2NameOffsetValue);
    }

    [Fact]
    [Trait("Category", "Overwrite")]
    [Trait("Category", "Write")]
    public void OverwriteContiguousTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var partition = new ExFatPartition(testEnvironment.PartitionStream);
        OverwriteTest(partition, DiskContent.LongContiguousFileName, offset => offset / 7);
        // now check nothing was overwritten
        PartitionReadTests.ReadFile(partition, DiskContent.LongSparseFile1Name,
            DiskContent.GetLongSparseFile1NameOffsetValue);
        PartitionReadTests.ReadFile(partition, DiskContent.LongSparseFile2Name,
            DiskContent.GetLongSparseFile2NameOffsetValue);
    }

    private static void AppendTest(ExFatPartition partition, string fileName, Func<ulong, ulong> getOffsetValue)
    {
        var fileEntry = partition.GetMetaEntries(partition.RootDirectoryDataDescriptor)
            .Single(e => e.ExtensionsFileName == fileName);
        var buffer = new byte[8];
        var dataDescriptor = fileEntry.DataDescriptor;
        using (var append = partition.OpenDataStream(dataDescriptor, FileAccess.ReadWrite))
        {
            var offset = (ulong) append.Seek(0, SeekOrigin.End);
            EndianUtilities.WriteBytesLittleEndian(getOffsetValue(offset), buffer);
            append.Write(buffer, 0, 8);
        }

        Assert.NotNull(dataDescriptor);
        
        using var read = partition.OpenDataStream(
            new DataDescriptor(dataDescriptor.Value.FirstCluster, false, DiskContent.LongFileSize + 8,
                DiskContent.LongFileSize + 8), FileAccess.Read);
        for (ulong offset = 0; offset < DiskContent.LongFileSize + 8; offset += 8)
        {
            var bytesRead = read.Read(buffer, 0, buffer.Length);
            Assert.Equal(bytesRead, buffer.Length);
            var readValue = EndianUtilities.ToUInt64LittleEndian(buffer);
            var expectedValue = getOffsetValue(offset);
            Assert.Equal(expectedValue, readValue);
        }

        Assert.Equal(0, read.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    [Trait("Category", "Append")]
    [Trait("Category", "Write")]
    public void AppendSparseTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var partition = new ExFatPartition(testEnvironment.PartitionStream);
        AppendTest(partition, DiskContent.LongSparseFile1Name,
            DiskContent.GetLongSparseFile1NameOffsetValue);
        // now check nothing was overwritten
        PartitionReadTests.ReadFile(partition, DiskContent.LongSparseFile2Name,
            DiskContent.GetLongSparseFile2NameOffsetValue);
    }

    [Fact]
    [Trait("Category", "Append")]
    [Trait("Category", "Write")]
    public void AppendContiguousTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var partition = new ExFatPartition(testEnvironment.PartitionStream);
        AppendTest(partition, DiskContent.LongContiguousFileName,
            DiskContent.GetLongContiguousFileNameOffsetValue);
        // now check nothing was overwritten
        PartitionReadTests.ReadFile(partition, DiskContent.LongSparseFile1Name,
            DiskContent.GetLongSparseFile1NameOffsetValue);
        PartitionReadTests.ReadFile(partition, DiskContent.LongSparseFile2Name,
            DiskContent.GetLongSparseFile2NameOffsetValue);
    }

    [Fact]
    [Trait("Category", "Create")]
    [Trait("Category", "Write")]
    public void CreateStreamTest()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var partition = new ExFatPartition(testEnvironment.PartitionStream);
        DataDescriptor? dataDescriptor = null;
        using (var stream = partition.CreateDataStream(d => dataDescriptor = d))
        {
            stream.WriteByte(1);
        }

        using var s2 = partition.OpenDataStream(dataDescriptor, FileAccess.Read);
        Assert.Equal(1, s2.ReadByte());
        Assert.Equal(-1, s2.ReadByte());
    }
}