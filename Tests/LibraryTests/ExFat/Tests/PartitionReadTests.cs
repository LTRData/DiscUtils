// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.Streams;
using System;
using System.Linq;
using System.IO;
using DiscUtils.ExFat.Internal.Partition;
using DiscUtils.ExFat.Internal;
using DiscUtils.ExFat.Internal.Partition.Entries;
using DiscUtils.ExFat.Internal.IO;
using LibraryTests.ExFat.Environment;

namespace LibraryTests.ExFat.Tests;
[Trait("Category", "Partition")]
public class PartitionReadTests
{
    internal static void ReadFile(string fileName, Func<ulong, ulong> getValueAtOffset,
        ulong? overrideLength = null, bool forward = true, bool forceSeek = false)
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        var fs = new ExFatPartition(testEnvironment.PartitionStream);
        ReadFile(fs, fileName, getValueAtOffset, overrideLength, forward, forceSeek);
    }

    internal static void ReadFile(ExFatPartition partition, string fileName, Func<ulong, ulong> getValueAtOffset,
        ulong? overrideLength = null, bool forward = true, bool forceSeek = false)
    {
        var fileEntry = partition.GetMetaEntries(partition.RootDirectoryDataDescriptor)
            .Single(e => e.ExtensionsFileName == fileName);
        var length = overrideLength ?? fileEntry.SecondaryStreamExtension.DataLength.Value;
        var contiguous =
            fileEntry.SecondaryStreamExtension.GeneralSecondaryFlags.Value.HasAny(ExFatGeneralSecondaryFlags
                .NoFatChain);
        using var stream = partition.OpenDataStream(
            new DataDescriptor(fileEntry.SecondaryStreamExtension.FirstCluster.Value, contiguous, length, length),
            FileAccess.Read);
        var vb = new byte[sizeof(ulong)];
        var range = Enumerable.Range(0, (int)(length / sizeof(ulong))).Select(r => r * sizeof(ulong));
        if (!forward)
        {
            range = range.Reverse();
            forceSeek = true;
        }

        foreach (var offset in range)
        {
            if (forceSeek)
            {
                stream.Seek(offset, SeekOrigin.Begin);
            }

            stream.ReadExactly(vb, 0, vb.Length);
            var v = EndianUtilities.ToUInt64LittleEndian(vb);
            Assert.Equal(v, getValueAtOffset((ulong)offset));
        }

        if (forward)
        {
            Assert.Equal(0, stream.Read(vb, 0, vb.Length));
        }
    }

    [Fact]
    [Trait("Category", "Read")]
    public void ReadLongContiguousFull() => ReadFile(DiskContent.LongContiguousFileName, DiskContent.GetLongContiguousFileNameOffsetValue);

    [Fact]
    [Trait("Category", "Read")]
    public void ReadLongContiguousFullBackwards()
    {
        ReadFile(DiskContent.LongContiguousFileName, DiskContent.GetLongContiguousFileNameOffsetValue,
            forward: false);
    }

    [Fact]
    [Trait("Category", "Read")]
    public void ReadLongContiguousLimited()
    {
        var length = (DiskContent.LongFileSize / 3 * 2) & ~7ul;
        ReadFile(DiskContent.LongContiguousFileName, DiskContent.GetLongContiguousFileNameOffsetValue, length);
    }

    [Fact]
    [Trait("Category", "Read")]
    public void ReadLongSparseFull() => ReadFile(DiskContent.LongSparseFile1Name, DiskContent.GetLongSparseFile1NameOffsetValue);

    [Fact]
    [Trait("Category", "Read")]
    public void ReadLongSparseFullBackwards() => ReadFile(DiskContent.LongSparseFile1Name, DiskContent.GetLongSparseFile1NameOffsetValue, forward: false);

    [Fact]
    [Trait("Category", "Read")]
    public void ReadLongSparseLimited()
    {
        var length = (DiskContent.LongFileSize / 3 * 2) & ~7ul;
        ReadFile(DiskContent.LongSparseFile1Name, DiskContent.GetLongSparseFile1NameOffsetValue, length);
    }
}