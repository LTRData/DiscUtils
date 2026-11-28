using System;
using DiscUtils.ReFs;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.SystemTables;
using static LibraryTests.ReFs.ReFsFixtureTest;

namespace LibraryTests.ReFs;

public class ReFsAllocatorSummaryTest
{
    // Free clusters and global stream reserve of checkpoint root 1's summary. Free minus reserve equals Windows' "Free Clusters" + "Total Reserved"
    // (fsutil fsinfo refsinfo, in each image's manifest) on all four: 0x5E9AE, 0x5789, 0x6536C and 0x51FAC.
    [FixtureFact("w2025-kitchen")]
    public void SpaceOfW2025Kitchen() => CheckSpace("w2025-kitchen", 4096, 0x7C000, 0x5E9AE, 0);

    [FixtureFact("w2022pre-plain-64k")]
    public void SpaceOfW2022PrePlain64k() => CheckSpace("w2022pre-plain-64k", 65536, 0x7C00, 0x5789, 0);

    [FixtureFact("w2016-kitchen")]
    public void SpaceOfW2016Kitchen() => CheckSpace("w2016-kitchen", 4096, 0x7C000, 0x6536C, 0);

    [FixtureFact("w2025-refsdedup-shared-lz4")]
    public void SpaceOfW2025RefsDedupSharedLz4() => CheckSpace("w2025-refsdedup-shared-lz4", 4096, 0x7C000, 0x51FAD, 1);

    private static void CheckSpace(string image, uint clusterSize, ulong totalClusters, ulong freeClusters, ulong streamReserve)
    {
        using var fs = OpenVolume(image, out var disk);
        using (disk)
        {
            var volume = fs.Volume;

            Assert.Equal(clusterSize, volume.Info.ClusterSize);
            Assert.Equal(totalClusters, volume.AllocatorSummary.Covered);
            Assert.Equal(streamReserve, volume.AllocatorSummary.GlobalStreamReserve);

            Assert.True(fs.SupportsUsedAvailableSpace);
            Assert.Equal((long)(totalClusters * clusterSize), fs.Size);
            Assert.Equal((long)((totalClusters - freeClusters) * clusterSize), fs.UsedSpace);
            Assert.Equal((long)((freeClusters - streamReserve) * clusterSize), fs.AvailableSpace);
        }
    }

    [Fact]
    public void TheFieldsAreReadFromTheirOffsets()
    {
        var summary = new AllocatorSummary(Summary(covered: 0x7C000, free: 0x5E9AE, reserve: 0x10, metadata: 0x1C608, length: 0x1A0));

        Assert.Equal(0x7C000UL, summary.Covered);
        Assert.Equal(0x5E9AEUL, summary.Free);
        Assert.Equal(0x10UL, summary.GlobalStreamReserve);
        Assert.Equal(0x1C608UL, summary.MetadataAllocated);
    }

    [Theory]
    [InlineData(0x100UL, 0x101UL, 0UL, 0UL)] // more free than covered
    [InlineData(0x100UL, 0x10UL, 0x101UL, 0UL)] // stream reserve above the covered clusters
    [InlineData(0x100UL, 0x10UL, 0UL, 0x101UL)] // metadata above the covered clusters
    public void ASummaryOutOfRangeIsRefused(ulong covered, ulong free, ulong reserve, ulong metadata)
    {
        Assert.Throws<ReFsFormatException>(() => new AllocatorSummary(Summary(covered, free, reserve, metadata, AllocatorSummary.MinimumLength)));
    }

    [Fact]
    public void AShortSummaryIsRefused()
    {
        Assert.Throws<ReFsFormatException>(() => new AllocatorSummary(new byte[AllocatorSummary.MinimumLength - 1]));
    }

    private static byte[] Summary(ulong covered, ulong free, ulong reserve, ulong metadata, int length)
    {
        var bytes = new byte[length];
        BitConverter.TryWriteBytes(bytes.AsSpan(0x00), 2UL);
        BitConverter.TryWriteBytes(bytes.AsSpan(0x08), covered);
        BitConverter.TryWriteBytes(bytes.AsSpan(0x10), free);
        BitConverter.TryWriteBytes(bytes.AsSpan(0x18), reserve);
        BitConverter.TryWriteBytes(bytes.AsSpan(0x20), metadata);
        return bytes;
    }
}