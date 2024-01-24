using System.IO;
using System.Linq;
using Xunit;
using DiscUtils.Wim;

namespace LibraryTests.Wim;

public class WinFileSystemTest
{
    private static string testDataPath = Path.Combine("..", "..", "LibraryTests", "Wim", "Data", "TestData.wim");

    [Fact]
    public void ReadRootDirectoryFiles()
    {
        using var fileStream = File.OpenRead(testDataPath);
        var wimFile = new WimFile(fileStream);
        var image = wimFile.GetImage(0);

        var files = image.GetFiles(image.Root.FullName, "*.*", SearchOption.AllDirectories).ToList();

        Assert.Equal(3, files.Count);
    }

    [Fact]
    public void ReadSubDirectoryFiles()
    {
        using var fileStream = File.OpenRead(testDataPath);
        var wimFile = new WimFile(fileStream);
        var image = wimFile.GetImage(0);

        var files = image.GetFiles("\\TestData", "*.*", SearchOption.AllDirectories).ToList();

        Assert.Equal(3, files.Count);
    }

    [Fact]
    public void ReadFilteredDirectory()
    {
        const string ExpectedDirectoryPath = "\\TestData\\Foo";

        using var fileStream = File.OpenRead(testDataPath);
        var wimFile = new WimFile(fileStream);
        var image = wimFile.GetImage(0);

        var directories = image.GetDirectories(image.Root.FullName, "Foo.*", SearchOption.AllDirectories).ToList();

        Assert.Single(directories);
        Assert.Equal(ExpectedDirectoryPath, directories[0]);
    }

    [Fact]
    public void ReadFilteredFile()
    {
        using var fileStream = File.OpenRead(testDataPath);
        var wimFile = new WimFile(fileStream);
        var image = wimFile.GetImage(0);

        var files = image.GetFiles("\\TestData\\Foo", "Lorem.*", SearchOption.AllDirectories).ToList();

        Assert.Equal(2, files.Count);
    }
}
