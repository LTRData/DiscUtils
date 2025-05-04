using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiscUtils.Iso9660;
using LibraryTests.Utilities;
using Xunit;

namespace LibraryTests.Iso9660;

public class SampleDataTests
{
    [Fact]
    public void AppleTestZip()
    {
        using var iso = Helpers.Helpers.LoadTestDataFileFromGZipFile(nameof(Iso9660), "apple-test.iso.gz");
        using var cr = new CDReader(iso, false);

        var dir = cr.GetDirectoryInfo("sub-directory");
        Assert.NotNull(dir);
        Assert.Equal("sub-directory", dir.Name);

        var files = dir.GetFiles("apple-test.txt").ToList();
        Assert.Single(files);
        Assert.Equal(21, files.First().Length);
        Assert.Equal("apple-test.txt", files.First().Name);
        Assert.Equal(dir, files.First().Directory);
    }

    [Fact]
    public void MultiExtentFiles()
    {
        using var iso = Helpers.Helpers.LoadTestDataFileFromGZipFile(nameof(Iso9660), "multiextent.iso_header.gz");
        using var cr = new CDReader(iso, joliet: true,  hideVersions: true);

        const string pathToMultiextentFiles = @"\PS3_GAME\USRDIR\Resource\Common";
        var fsEntries = cr.GetFileSystemEntries(pathToMultiextentFiles).ToList();
        Assert.Equal(11, fsEntries.Count);

        var dir = cr.GetDirectoryInfo(pathToMultiextentFiles);
        var files = dir.GetFiles().ToList();
        Assert.Equal(10, files.Count);

        var misc0 = files.First(f => f.Name is "Misc0.FPK");
        var misc1 = files.First(f => f.Name is "Misc1.FPK");
        Assert.Equal(1464404972, misc0.Length);
        Assert.Equal(1585521232, misc1.Length);
        
        var meClusters = cr.PathToClusters(misc0.FullName).ToList();
        Assert.Equal(2, meClusters.Count);
    }
}