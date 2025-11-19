// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.ExFat.Internal.IO;
using DiscUtils.ExFat.Internal.Partition;
using LibraryTests.ExFat.Environment;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryTests.ExFat.Tests;
[Trait("Category", "Structure")]
public class PartitionClustersTests
{
    [Fact]
    [Trait("Category", "Structure")]
    public void ReadLongFileClusters()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        using var partition = new ExFatPartition(testEnvironment.PartitionStream);
        var oneM = partition.GetMetaEntries(partition.RootDirectoryDataDescriptor)
            .Single(e => e.ExtensionsFileName == DiskContent.LongSparseFile1Name);
        var clusters = new List<Cluster>();
        for (Cluster c = oneM.SecondaryStreamExtension.FirstCluster.Value; ; c = partition.GetNextCluster(c))
        {
            if (c.IsLast)
            {
                break;
            }

            if (!c.IsData)
            {
                throw new Exception("Found invalid cluster (o'brother, where art thou?)");
            }

            clusters.Add(c);
        }
    }
}