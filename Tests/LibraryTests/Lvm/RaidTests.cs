using DiscUtils;
using DiscUtils.Complete;
using DiscUtils.Streams;
using DiscUtils.Vhdx;

namespace LibraryTests.Lvm;

public class RaidTests
{
    [Fact]
    public void RecognizeMirror()
    {
        SetupHelper.SetupComplete();
        using var vhdx = Helpers.Helpers.LoadTestDataFileFromGZipFile("Lvm", "raidtest.vhdx.gz");
        using var diskImage = new DiskImageFile(vhdx, Ownership.Dispose);
        using var disk = new Disk([diskImage], Ownership.Dispose);
        var manager = new VolumeManager(disk);
        
        var logicalVolumes = manager.GetLogicalVolumes();
        
        Assert.Single(logicalVolumes);

        Assert.Equal("Linux RAID 1 (Mirror)", logicalVolumes[0].TypeAsString);

        Assert.Equal(50331648, logicalVolumes[0].Length);
    }
}
