// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.ExFat.Internal.Partition;
using DiscUtils.ExFat.Internal.Partition.Entries;
using LibraryTests.ExFat.Environment;
using System.Linq;

namespace LibraryTests.ExFat.Tests;

[Trait("Category", "Partition")]
public class PartitionStructureTests
{
    [Fact]
    [Trait("Category", "Structure")]
    public void DirectoryEntries()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        var partition = new ExFatPartition(testEnvironment.PartitionStream);
        var entries = partition.GetEntries(partition.RootDirectoryDataDescriptor).ToArray();
        Assert.Contains(entries.OfType<FileNameExtensionExFatDirectoryEntry>()
, e => e.FileName.Value == DiskContent.LongContiguousFileName);
    }

    [Fact]
    [Trait("Category", "Structure")]
    public void ValidGroupedEntries()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        var partition = new ExFatPartition(testEnvironment.PartitionStream);
        var entries = partition.GetMetaEntries(partition.RootDirectoryDataDescriptor).ToArray();
        Assert.Contains(entries, e => e.ExtensionsFileName == DiskContent.LongContiguousFileName);
    }

    [Fact]
    [Trait("Category", "Structure")]
    public void CheckHashes()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        var partition = new ExFatPartition(testEnvironment.PartitionStream);
        foreach (var entry in partition.GetMetaEntries(partition.RootDirectoryDataDescriptor))
        {
            if (entry.Primary is FileExFatDirectoryEntry)
            {
                var hash = partition.ComputeNameHash(entry.ExtensionsFileName);
                Assert.Equal(entry.SecondaryStreamExtension.NameHash.Value, hash);
            }
        }
    }

    [Fact]
    [Trait("Category", "Structure")]
    public void CheckChecksums()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        var partition = new ExFatPartition(testEnvironment.PartitionStream);
        foreach (var entry in partition.GetMetaEntries(partition.RootDirectoryDataDescriptor))
        {
            if (entry.Primary is FileExFatDirectoryEntry fileEntry)
            {
                var checksum = fileEntry.ComputeChecksum(entry.Secondaries);
                Assert.Equal(fileEntry.SetChecksum.Value, checksum);
            }
        }
    }

#if nomore
    [Fact]
    [Trait("Category", "Structure")]
    public void AllocationBitmapExists()
    {
        using (var testEnvironment = StreamTestEnvironment.FromExistingVhdx())
        {
            var partition = new ExFatPartition(testEnvironment.PartitionStream);
            var bitmap = partition.GetAllocationBitmap();
            Assert.True(bitmap[2]);
            var allocate1 = bitmap.FindUnallocated();
            Assert.False(bitmap[allocate1]);
            var allocate10 = bitmap.FindUnallocated(10);
            Assert.False(bitmap[allocate10]);
            Assert.False(bitmap[allocate10 + 1]);
            Assert.False(bitmap[allocate10 + 2]);
            Assert.False(bitmap[allocate10 + 3]);
            Assert.False(bitmap[allocate10 + 4]);
            Assert.False(bitmap[allocate10 + 5]);
            Assert.False(bitmap[allocate10 + 6]);
            Assert.False(bitmap[allocate10 + 7]);
            Assert.False(bitmap[allocate10 + 8]);
            Assert.False(bitmap[allocate10 + 9]);
        }
    }
#endif
}