using System.IO;
using System.Linq;
using System.Text;
using DiscUtils;
using DiscUtils.Ext;

namespace LibraryTests.Ext;

public class ExtFileSystemTest
{
    [Fact]
    public void LoadFileSystem()
    {
        using var data = Helpers.Helpers.LoadTestDataFileFromGZipFile("Ext", "data.ext4.dat.gz");
        using var fs = new ExtFileSystem(data, new FileSystemParameters());

        Assert.Collection(fs.Root.GetFileSystemInfos()
                            .OrderBy(static s => s.Name),
            static s =>
            {
                Assert.Equal("bar", s.Name);
                Assert.NotEqual<FileAttributes>(0, s.Attributes & FileAttributes.Directory);
            },
            static s =>
            {
                Assert.Equal("foo", s.Name);
                Assert.NotEqual<FileAttributes>(0, s.Attributes & FileAttributes.Directory);
            },
            static s =>
            {
                Assert.Equal("lost+found", s.Name);
                Assert.NotEqual<FileAttributes>(0, s.Attributes & FileAttributes.Directory);
            });

        Assert.Empty(fs.Root.GetDirectories("foo").First().GetFileSystemInfos());

        Assert.Collection(fs.Root.GetDirectories("bar").First().GetFileSystemInfos()
                            .OrderBy(static s => s.Name),
            static s =>
            {
                Assert.Equal("blah.txt", s.Name);
                Assert.Equal<FileAttributes>(0, s.Attributes & FileAttributes.Directory);
            },
            static s =>
            {
                Assert.Equal("testdir1", s.Name);
                Assert.NotEqual<FileAttributes>(0, s.Attributes & FileAttributes.Directory);
            });

        var sep = Path.DirectorySeparatorChar;

        var tmpData = fs.ReadAllBytes($"bar{sep}blah.txt");
        Assert.Equal(Encoding.ASCII.GetBytes("hello world\n"), tmpData);

        tmpData = fs.ReadAllBytes($"bar{sep}testdir1{sep}test.txt");
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

        var dir4 = fs.GetDirectoryInfo("dir4");

        Assert.True(dir4.Exists);

        Assert.Single(dir4.GetFiles("file4"));

        var dir1 = fs.GetDirectoryInfo("dir1");
        
        Assert.True(dir1.Exists);

        var dir1_link4rel = dir1.GetDirectories("link4rel").ToArray();
        
        Assert.Single(dir1_link4rel);

        Assert.Single(dir1_link4rel[0].GetFiles("file4"));

        var dir1_link4abs = dir1.GetDirectories("link4abs").ToArray();
        
        Assert.Single(dir1_link4abs[0].GetFiles("file4"));

        Assert.Equal(4, fs.GetDirectories("link1rel").Count());

        Assert.Equal(4, fs.GetDirectories("link1abs").Count());

        var link1rel = fs.GetDirectoryInfo("link1rel");
        
        Assert.True(link1rel.Exists);

        var link1rel_dir3 = link1rel.GetDirectories("dir3").ToArray();
        
        Assert.Single(link1rel_dir3);
        
        Assert.Single(link1rel_dir3[0].GetDirectories("link2abs"));

        var link1abs = fs.GetDirectoryInfo("link1abs");
        
        Assert.True(link1abs.Exists);

        var link1abs_dir3 = link1abs.GetDirectories("dir3").ToArray();
        
        Assert.Single(link1abs_dir3);
        
        Assert.Single(link1abs_dir3[0].GetDirectories("link2abs"));

        var link1rel_link4abs = link1rel.GetDirectories("link4abs").ToArray();
        
        Assert.Single(link1rel_link4abs);
        
        Assert.Single(link1rel_link4abs[0].GetFiles("file4"));

        var link1abs_link4rel = link1abs.GetDirectories("link4rel").ToArray();
        
        Assert.Single(link1abs_link4rel);
        
        Assert.Single(link1abs_link4rel[0].GetFiles("file4"));
    }

    [Fact]
    public void TestLinksWithWildcards()
    {
        using var data = Helpers.Helpers.LoadTestDataFileFromGZipFile("Ext", "data.ext4.links.gz");
        using var fs = new ExtFileSystem(data, new FileSystemParameters());

        var dir4 = fs.GetDirectoryInfo("dir4");

        Assert.True(dir4.Exists);

        Assert.Single(dir4.GetFiles("file*"));

        var dir1 = fs.GetDirectoryInfo("dir1");

        Assert.True(dir1.Exists);

        var dir1_link4rel = dir1.GetDirectories("link4r*").ToArray();

        Assert.Single(dir1_link4rel);

        Assert.Single(dir1_link4rel[0].GetFiles("file*"));

        var dir1_link4abs = dir1.GetDirectories("link4a*").ToArray();

        Assert.Single(dir1_link4abs[0].GetFiles("file*"));

        Assert.Equal(4, fs.GetDirectories("link1rel").Count());

        Assert.Equal(4, fs.GetDirectories("link1abs").Count());

        var link1rel = fs.GetDirectoryInfo("link1rel");

        Assert.True(link1rel.Exists);

        var link1rel_dir3 = link1rel.GetDirectories("*3").ToArray();

        Assert.Single(link1rel_dir3);

        Assert.Single(link1rel_dir3[0].GetDirectories("link2a*"));

        var link1abs = fs.GetDirectoryInfo("link1abs");

        Assert.True(link1abs.Exists);

        var link1abs_dir3 = link1abs.GetDirectories("*3").ToArray();

        Assert.Single(link1abs_dir3);

        Assert.Single(link1abs_dir3[0].GetDirectories("link2a*"));

        var link1rel_link4abs = link1rel.GetDirectories("link4a*").ToArray();

        Assert.Single(link1rel_link4abs);

        Assert.Single(link1rel_link4abs[0].GetFiles("file*"));

        var link1abs_link4rel = link1abs.GetDirectories("link4r*").ToArray();

        Assert.Single(link1abs_link4rel);

        Assert.Single(link1abs_link4rel[0].GetFiles("file*"));
    }

    [Fact]
    public void TestLinksSubFolderSearch()
    {
        using var data = Helpers.Helpers.LoadTestDataFileFromGZipFile("Ext", "data.ext4.links.gz");
        using var fs = new ExtFileSystem(data, new FileSystemParameters());

        var filesFound = fs.GetFiles("/link1rel", "file*", SearchOption.AllDirectories).ToArray();

        Assert.Equal(6, filesFound.Length);

        var file4Found = fs.GetFiles("/link1rel", "file4", SearchOption.AllDirectories).ToArray();

        Assert.Equal(6, file4Found.Length);
    }
}
