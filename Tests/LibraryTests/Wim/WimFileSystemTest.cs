using System.IO;
using System.Linq;
using Xunit;
using DiscUtils.Wim;
using System.Security.Permissions;
using System;
using LibraryTests.Helpers;
using System.Security.Cryptography;
using DiscUtils.Streams;

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
    public void ReadFileData()
    {
        const string path = "\\TestData\\Lorem.txt";

        using var fileStream = File.OpenRead(testDataPath);
        var wimFile = new WimFile(fileStream);
        var image = wimFile.GetImage(0);

        var storedHash = image.GetFileHash(path);

        var data = image.ReadAllBytes(path);

#if NET7_0_OR_GREATER
        Span<byte> calcHash = stackalloc byte[SHA1.HashSizeInBytes];
        SHA1.HashData(data, calcHash);
        Assert.Equal(storedHash.AsSpan(), calcHash);
#else
        using var sha1 = SHA1.Create();
        var calcHash = sha1.ComputeHash(data);
        Assert.Equal(storedHash, calcHash);
#endif
    }

    [Fact]
    public void TryOpenMissingFile()
    {
        const string path = "\\nonexist.txt";

        using var fileStream = File.OpenRead(testDataPath);
        var wimFile = new WimFile(fileStream);
        var image = wimFile.GetImage(0);

        Assert.Throws<FileNotFoundException>(() => image.GetFileHash(path));

        Assert.Throws<FileNotFoundException>(() => image.OpenFile(path, FileMode.Open, FileAccess.Read).Close());
    }

    [Fact]
    public void TryOpenDirAsFile()
    {
        using var fileStream = File.OpenRead(testDataPath);
        var wimFile = new WimFile(fileStream);
        var image = wimFile.GetImage(0);

        Assert.Throws<InvalidOperationException>(() => image.OpenFile("\\TestData", FileMode.Open, FileAccess.Read).Close());
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
        var ExpectedDirectoryPath = @$"{Path.DirectorySeparatorChar}TestData{Path.DirectorySeparatorChar}Foo";

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
