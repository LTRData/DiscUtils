using System.IO;
using System.Linq;
using DiscUtils.Iso9660;
using LibraryTests.Utilities;
using Xunit;

namespace LibraryTests.Iso9660;

public class SampleDataTests
{
    [Fact]
    public void AppleTestZip()
    {
        using var iso = Helpers.Helpers.LoadTestDataFileFromGZipFile("Iso9660", "apple-test.iso.gz");
        using var cr = new CDReader(iso, false);

        var dir = cr.GetDirectoryInfo("sub-directory");
        Assert.NotNull(dir);
        Assert.Equal("sub-directory", dir.Name);

        var file = dir.GetFiles("apple-test.txt");
        Assert.Single(file);
        Assert.Equal(21, file.First().Length);
        Assert.Equal("apple-test.txt", file.First().Name);
        Assert.Equal(dir, file.First().Directory);
    }
}