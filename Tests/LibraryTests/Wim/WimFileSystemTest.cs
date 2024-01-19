using System.IO;
using System.Linq;
using Xunit;
using DiscUtils.Wim;

namespace LibraryTests.Wim;

public class WinFileSystemTest

{
    [Fact]
    public void ReadRootDirectory()
    {
        using var fileStream = File.OpenRead(Path.Combine("..", "..", "LibraryTests", "Wim", "Data", "TestData.wim"));
        var wimFile = new WimFile(fileStream);
        var image = wimFile.GetImage(0);

        var files = image.GetFiles(image.Root.FullName, "*.*", SearchOption.AllDirectories).ToList();
        Assert.Equal(3, files.Count);
    }
}
