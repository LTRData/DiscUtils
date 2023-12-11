using System.IO;
using System.Linq;
using System.Text;
using DiscUtils;
using DiscUtils.Ext;
using LibraryTests.Helpers;
using Xunit;

namespace LibraryTests.Ext
{
    public class ExtFileSystemTest
    {
        [Fact]
        public void LoadFileSystem()
        {
            using var data = Helpers.Helpers.LoadDataFile("data.ext4.dat");
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
        }
    }
}
