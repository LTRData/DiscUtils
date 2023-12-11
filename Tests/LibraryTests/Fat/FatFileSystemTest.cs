//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.IO;
using System.Linq;
using System.Text;
using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Setup;
using DiscUtils.Streams;
using Xunit;

namespace LibraryTests.Fat
{
    public class FatFileSystemTest
    {
        [Fact]
        public void FormatFloppy()
        {
            var ms = new MemoryStream();
            var fs = FatFileSystem.FormatFloppy(ms, FloppyDiskType.HighDensity, "KBFLOPPY   ");
        }

        [Fact(Skip = "Saving LFN not yet implemented")]
        public void Cyrillic()
        {
            SetupHelper.RegisterAssembly(typeof(FatFileSystem).Assembly);

            var lowerDE = "\x0434";
            var upperDE = "\x0414";

            var ms = new MemoryStream();
            using (var fs = FatFileSystem.FormatFloppy(ms, FloppyDiskType.HighDensity, "KBFLOPPY   "))
            {
                fs.FatOptions.FileNameEncoding = Encoding.GetEncoding(855);

                var name = lowerDE;
                fs.CreateDirectory(name);

                var dirs = fs.GetDirectories("").ToArray();
                Assert.Single(dirs);
                Assert.Equal(upperDE, fs.GetShortName(dirs[0])); // Uppercase

                Assert.True(fs.DirectoryExists(lowerDE));
                Assert.True(fs.DirectoryExists(upperDE));

                fs.CreateDirectory(lowerDE + lowerDE + lowerDE);
                Assert.Equal(2, fs.GetDirectories("").Count());

                fs.DeleteDirectory(lowerDE + lowerDE + lowerDE);
                Assert.Single(fs.GetDirectories(""));
            }

            var detectDefaultFileSystems = FileSystemManager.DetectFileSystems(ms);

            var fs2 = detectDefaultFileSystems[0].Open(
                ms,
                new FileSystemParameters { FileNameEncoding = Encoding.GetEncoding(855) });

            Assert.True(fs2.DirectoryExists(lowerDE));
            Assert.True(fs2.DirectoryExists(upperDE));
            Assert.Single(fs2.GetDirectories(""));
        }

        [Fact]
        public void DefaultCodepage()
        {
            var graphicChar = "\x255D";

            var ms = new MemoryStream();
            var fs = FatFileSystem.FormatFloppy(ms, FloppyDiskType.HighDensity, "KBFLOPPY   ");
            fs.FatOptions.FileNameEncoding = Encoding.GetEncoding(855);

            var name = graphicChar;
            fs.CreateDirectory(name);

            var dirs = fs.GetDirectories("").ToArray();
            Assert.Single(dirs);
            Assert.Equal(graphicChar, dirs[0]); // Uppercase

            Assert.True(fs.DirectoryExists(graphicChar));
        }

        [Fact]
        public void FormatPartition()
        {
            var ms = new MemoryStream();

            var g = Geometry.FromCapacity(1024 * 1024 * 32);
            var fs = FatFileSystem.FormatPartition(ms, "KBPARTITION", g, 0, (int)g.TotalSectorsLong, 13);

            fs.CreateDirectory(@"DIRB\DIRC");

            var fs2 = new FatFileSystem(ms);
            Assert.Single(fs2.Root.GetDirectories());
        }

        [Fact]
        public void CreateDirectory()
        {
            var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");

            fs.CreateDirectory(@"UnItTeSt");
            var entry = fs.Root.GetDirectories("UNITTEST").First();
            Assert.Equal("UnItTeSt", entry.Name);
            Assert.Equal("UNITTEST", fs.GetShortName(entry.FullName));

            fs.CreateDirectory(@"folder\subflder");
            Assert.Equal("FOLDER", fs.GetShortName(fs.Root.GetDirectories("FOLDER").First().FullName));

            fs.CreateDirectory(@"folder\subflder");
            Assert.Equal("SUBFLDER", fs.GetShortName(fs.Root.GetDirectories("FOLDER").First().GetDirectories("SUBFLDER").First().FullName));

        }

        [Fact]
        public void CanWrite()
        {
            var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");
            Assert.True(fs.CanWrite);
        }

        [Fact]
        public void Label()
        {
            var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");
            Assert.Equal("FLOPPY_IMG ", fs.VolumeLabel);

            fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, null);
            Assert.Equal("NO NAME    ", fs.VolumeLabel);
        }

        [Fact]
        public void FileInfo()
        {
            var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");
            var fi = fs.GetFileInfo(@"SOMEDIR\SOMEFILE.TXT");
            Assert.NotNull(fi);
        }

        [Fact]
        public void DirectoryInfo()
        {
            var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");
            var fi = fs.GetDirectoryInfo(@"SOMEDIR");
            Assert.NotNull(fi);
        }

        [Fact]
        public void FileSystemInfo()
        {
            var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");
            var fi = fs.GetFileSystemInfo(@"SOMEDIR\SOMEFILE");
            Assert.NotNull(fi);
        }

        [Fact]
        public void Root()
        {
            var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");
            Assert.NotNull(fs.Root);
            Assert.True(fs.Root.Exists);
            Assert.Empty(fs.Root.Name);
            Assert.Null(fs.Root.Parent);
        }

        [Fact]
        [Trait("Category", "ThrowsException")]
        public void OpenFileAsDir()
        {
            var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");

            using (var s = fs.OpenFile("FOO.TXT", FileMode.Create, FileAccess.ReadWrite))
            {
                var w = new StreamWriter(s);
                w.WriteLine("FOO - some sample text");
                w.Flush();
            }

            Assert.Throws<DirectoryNotFoundException>(() => fs.GetFiles("FOO.TXT").Any());
        }

        [Fact]
        public void HonoursReadOnly()
        {
            var sep = Path.DirectorySeparatorChar;

            var diskStream = new SparseMemoryStream();
            var fs = FatFileSystem.FormatFloppy(diskStream, FloppyDiskType.HighDensity, "FLOPPY_IMG ");

            fs.CreateDirectory(@"AAA");
            fs.CreateDirectory(@"BAR");
            using (Stream t = fs.OpenFile($"BAR{sep}AAA.TXT", FileMode.Create, FileAccess.ReadWrite)) { }
            using (var s = fs.OpenFile($"BAR{sep}FOO.TXT", FileMode.Create, FileAccess.ReadWrite))
            {
                var w = new StreamWriter(s);
                w.WriteLine("FOO - some sample text");
                w.Flush();
            }
            fs.SetLastAccessTimeUtc($"BAR", new DateTime(1980, 1, 1));
            fs.SetLastAccessTimeUtc($"BAR{sep}FOO.TXT", new DateTime(1980, 1, 1));

            // Check we can access a file without any errors
            var roDiskStream = SparseStream.ReadOnly(diskStream, Ownership.None);
            var fatFs = new FatFileSystem(roDiskStream);
            using Stream fileStream = fatFs.OpenFile($"BAR{sep}FOO.TXT", FileMode.Open);
            fileStream.ReadByte();
        }

        [Fact]
        public void InvalidImageThrowsException()
        {
            var stream = new SparseMemoryStream();
            var buffer = new byte[1024 * 1024];
            stream.Write(buffer, 0, 1024 * 1024);
            stream.Position = 0;
            Assert.Throws<InvalidFileSystemException>(() => new FatFileSystem(stream));
        }
    }
}
