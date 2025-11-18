// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.ExFat;
using LibraryTests.ExFat.Environment;

namespace LibraryTests.ExFat.Tests;
[Trait("Category", "Partition")]
public class IntegrityTests
{
    [Fact]
    [Trait("Category", "Detection")]
    public void ValidVolume()
    {
        using var testEnvironment = StreamTestEnvironment.FromExistingVhdx();
        Assert.True(ExFatFileSystem.Detect(testEnvironment.PartitionStream));
    }
}