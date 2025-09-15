using System.IO;
using System.Linq;
using System.Text;
using DiscUtils;
using DiscUtils.Ext;
using LibraryTests.Helpers;
using Xunit;

namespace LibraryTests.Ext;

public class ExtFileSystemTest
{
    [Fact]
    public void LoadFileSystem()
    {
        using var data = Helpers.Helpers.LoadTestDataFileFromGZipFile("Ext", "data.ext4.dat.gz");
        using var fs = new ExtFileSystem(data, new FileSystemParameters());

        Assert.Collection(fs.Root.GetFileSystemInfos()
                            .OrderBy(s => s.Name),
            s =>
            {
                Assert.Equal("bar", s.Name);
                Assert.NotEqual<FileAttributes>(0, s.Attributes & FileAttributes.Directory);
            },
            s =>
            {
                Assert.Equal("foo", s.Name);
                Assert.NotEqual<FileAttributes>(0, s.Attributes & FileAttributes.Directory);
            },
            s =>
            {
                Assert.Equal("lost+found", s.Name);
                Assert.NotEqual<FileAttributes>(0, s.Attributes & FileAttributes.Directory);
            });

        Assert.Empty(fs.Root.GetDirectories("foo").First().GetFileSystemInfos());

        Assert.Collection(fs.Root.GetDirectories("bar").First().GetFileSystemInfos()
                            .OrderBy(s => s.Name),
            s =>
            {
                Assert.Equal("blah.txt", s.Name);
                Assert.Equal<FileAttributes>(0, s.Attributes & FileAttributes.Directory);
            },
            s =>
            {
                Assert.Equal("testdir1", s.Name);
                Assert.NotEqual<FileAttributes>(0, s.Attributes & FileAttributes.Directory);
            });

        var sep = Path.DirectorySeparatorChar;

        var tmpData = fs.OpenFile($"bar{sep}blah.txt", FileMode.Open).ReadAll();
        Assert.Equal(Encoding.ASCII.GetBytes("hello world\n"), tmpData);

        tmpData = fs.OpenFile($"bar{sep}testdir1{sep}test.txt", FileMode.Open).ReadAll();
        Assert.Equal(Encoding.ASCII.GetBytes("Mon Feb 11 19:54:14 UTC 2019\n"), tmpData);

        Assert.Single(fs.GetFiles("bar", "b*"));
    }

    [Fact]
    public void TestLinks()
    {
        using var data = Helpers.Helpers.LoadTestDataFileFromGZipFile("Ext", "data.ext4.links.gz");
        using var fs = new ExtFileSystem(data, new FileSystemParameters());

        Assert.True(fs.FileExists("/dir4/file4"));

        Assert.True(fs.FileExists("/dir1/link4rel/file4"));

        Assert.True(fs.FileExists("/dir1/link4abs/file4"));

        Assert.True(fs.DirectoryExists("/link1rel"));

        Assert.True(fs.DirectoryExists("/link1abs"));

        Assert.True(fs.DirectoryExists("/link1rel/dir3/link2abs"));

        Assert.True(fs.DirectoryExists("/link1abs/dir3/link2abs"));

        Assert.True(fs.FileExists("/link1rel/link4abs/file4"));

        Assert.True(fs.FileExists("/link1abs/link4rel/file4"));
    }

    [Fact]
    public void TestLinksWithInfo()
    {
        using var data = Helpers.Helpers.LoadTestDataFileFromGZipFile("Ext", "data.ext4.links.gz");
        using var fs = new ExtFileSystem(data, new FileSystemParameters());

        Assert.NotEmpty(fs.GetDirectoryInfo("dir4").GetFiles("file4"));

        Assert.NotEmpty(fs.GetDirectoryInfo("dir1").GetDirectories("link4rel").First().GetFiles("file4"));

        Assert.NotEmpty(fs.GetDirectoryInfo("dir1").GetDirectories("link4abs").First().GetFiles("file4"));

        Assert.NotEmpty(fs.GetDirectories("link1rel"));

        Assert.NotEmpty(fs.GetDirectories("link1abs"));

        Assert.NotEmpty(fs.GetDirectoryInfo("link1rel").GetDirectories("dir3").First().GetDirectories("link2abs"));

        Assert.NotEmpty(fs.GetDirectoryInfo("link1abs").GetDirectories("dir3").First().GetDirectories("link2abs"));

        Assert.NotEmpty(fs.GetDirectoryInfo("link1rel").GetDirectories("link4abs").First().GetFiles("file4"));

        Assert.NotEmpty(fs.GetDirectoryInfo("link1abs").GetDirectories("link4rel").First().GetFiles("file4"));
    }
}
