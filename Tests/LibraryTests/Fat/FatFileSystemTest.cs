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

namespace LibraryTests.Fat;

public class FatFileSystemTest
{
    [Fact]
    public void FormatFloppy()
    {
        var ms = new MemoryStream();
        var fs = FatFileSystem.FormatFloppy(ms, FloppyDiskType.HighDensity, "KBFLOPPY   ");
    }

    [Fact]
    public void Cyrillic()
    {
        SetupHelper.RegisterAssembly(typeof(FatFileSystem).Assembly);

        var lowerDE = "\x0434";
        var upperDE = "\x0414";

        using var ms = new MemoryStream();
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

        using var ms = new MemoryStream();
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
        using var ms = new MemoryStream();

        var g = Geometry.FromCapacity(1024 * 1024 * 32);
        var fs = FatFileSystem.FormatPartition(ms, "KBPARTITION", g, 0, (int)g.TotalSectorsLong, 13);

        fs.CreateDirectory(@$"DIRB{Path.DirectorySeparatorChar}DIRC");

        var fs2 = new FatFileSystem(ms);
        Assert.Single(fs2.Root.GetDirectories());
    }

    [Fact]
    public void CreateDirectory()
    {
        using var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");

        fs.CreateDirectory(@"UnItTeSt");
        var entry = fs.Root.GetDirectories("UNITTEST").First();
        Assert.Equal("UnItTeSt", entry.Name);
        Assert.Equal("UNITTEST", fs.GetShortName(entry.FullName));

        fs.CreateDirectory(@$"folder{Path.DirectorySeparatorChar}subflder");
        Assert.Equal("FOLDER", fs.GetShortName(fs.Root.GetDirectories("FOLDER").First().FullName));

        fs.CreateDirectory(@$"folder{Path.DirectorySeparatorChar}subflder");
        Assert.Equal("SUBFLDER", fs.GetShortName(fs.Root.GetDirectories("FOLDER").First().GetDirectories("SUBFLDER").First().FullName));

    }

    [Fact]
    public void CanWrite()
    {
        using var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");
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
        using var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");
        var fi = fs.GetFileInfo(@$"SOMEDIR{Path.DirectorySeparatorChar}SOMEFILE.TXT");
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
        using var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");
        var fi = fs.GetFileSystemInfo(@$"SOMEDIR{Path.DirectorySeparatorChar}SOMEFILE");
        Assert.NotNull(fi);
    }

    [Fact]
    public void Root()
    {
        using var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");
        Assert.NotNull(fs.Root);
        Assert.True(fs.Root.Exists);
        Assert.Empty(fs.Root.Name);
        Assert.Null(fs.Root.Parent);
    }

    [Fact]
    [Trait("Category", "ThrowsException")]
    public void OpenFileAsDir()
    {
        using var fs = FatFileSystem.FormatFloppy(new MemoryStream(), FloppyDiskType.HighDensity, "FLOPPY_IMG ");

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

        using var diskStream = new SparseMemoryStream();
        var fs = FatFileSystem.FormatFloppy(diskStream, FloppyDiskType.HighDensity, "FLOPPY_IMG ");

        fs.CreateDirectory(@"AAA");
        fs.CreateDirectory(@"BAR");
        using (var t = fs.OpenFile($"BAR{sep}AAA.TXT", FileMode.Create, FileAccess.ReadWrite))
        {
        }

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
        using var fileStream = fatFs.OpenFile($"BAR{sep}FOO.TXT", FileMode.Open);
        fileStream.ReadByte();
    }

    [Fact]
    public void InvalidImageThrowsException()
    {
        using var stream = new SparseMemoryStream();
        var buffer = new byte[1024 * 1024];
        stream.Write(buffer, 0, 1024 * 1024);
        stream.Position = 0;
        Assert.Throws<InvalidFileSystemException>(() => new FatFileSystem(stream));
    }

    [Fact]
    public void TestShortNameDeletedEntries()
    {
        using var diskStream = new SparseMemoryStream();

        {
            using var fs = FatFileSystem.FormatFloppy(diskStream, FloppyDiskType.HighDensity, "FLOPPY_IMG ");

            fs.CreateDirectory(@"FOO1");
            fs.CreateDirectory(@"FOO2");
            fs.CreateDirectory(@"FOO3");
            fs.CreateDirectory(@"FOO4");
            fs.CreateDirectory(@"BAR");
            fs.CreateDirectory(@"BAR1");
            fs.CreateDirectory(@"BAR2");
            fs.CreateDirectory(@"BAR3");

            fs.DeleteDirectory(@"FOO1");
            fs.DeleteDirectory(@"FOO2");
            fs.DeleteDirectory(@"FOO3");
            fs.DeleteDirectory(@"FOO4");
            fs.DeleteDirectory(@"BAR1");
            fs.DeleteDirectory(@"BAR2");
            fs.DeleteDirectory(@"BAR3");
            fs.CreateDirectory(@"01234567890123456789.txt");
        }

        {
            var fs = new FatFileSystem(diskStream);
            var entries = fs.GetFileSystemEntries(Path.DirectorySeparatorChar.ToString()).OrderBy(static x => x).ToList();
            Assert.Equal(2, entries.Count);
            Assert.Equal($"{Path.DirectorySeparatorChar}01234567890123456789.txt", entries[0]);
            Assert.Equal($"{Path.DirectorySeparatorChar}BAR", entries[1]);

            fs.CreateDirectory("abcdefghijklmnop.txt");

            entries = fs.GetFileSystemEntries(Path.DirectorySeparatorChar.ToString()).OrderBy(static x => x).ToList();
            Assert.Equal(3, entries.Count);
            Assert.Equal(Path.DirectorySeparatorChar + "01234567890123456789.txt", entries[0]);
            Assert.Equal(Path.DirectorySeparatorChar + "abcdefghijklmnop.txt", entries[1]);
            Assert.Equal(Path.DirectorySeparatorChar + "BAR", entries[2]);
        }
    }
    
    [Fact]
    public void TestLongNameDeletedEntries()
    {
        using var diskStream = new SparseMemoryStream();

        {
            using var fs = FatFileSystem.FormatFloppy(diskStream, FloppyDiskType.HighDensity, "FLOPPY_IMG ");

            fs.CreateDirectory(@"FOO_This_is_a_long_entry_1");
            fs.CreateDirectory(@"FOO_This_is_a_long_entry_2");
            fs.CreateDirectory(@"FOO_This_is_a_long_entry_3");
            fs.CreateDirectory(@"FOO_This_is_a_long_entry_4");

            fs.DeleteDirectory(@"FOO_This_is_a_long_entry_1"); // 26 characters, should take 3 entries (2 for LFN + 1 for SFN)
            fs.CreateDirectory("TA"); // Should take the entry of the previously deleted entry
            fs.CreateDirectory("TB");
            fs.CreateDirectory("TC");
        }

        {
            var fs = new FatFileSystem(diskStream);
            var entries = fs.GetFileSystemEntries(Path.DirectorySeparatorChar.ToString()).OrderBy(static x => x).ToList();
            Assert.Equal(6, entries.Count);
            Assert.Equal($"{Path.DirectorySeparatorChar}FOO_This_is_a_long_entry_2", entries[0]);
            Assert.Equal($"{Path.DirectorySeparatorChar}FOO_This_is_a_long_entry_3", entries[1]);
            Assert.Equal($"{Path.DirectorySeparatorChar}FOO_This_is_a_long_entry_4", entries[2]);
            Assert.Equal($"{Path.DirectorySeparatorChar}TA", entries[3]);
            Assert.Equal($"{Path.DirectorySeparatorChar}TB", entries[4]);
            Assert.Equal($"{Path.DirectorySeparatorChar}TC", entries[5]);
        }
    }

    [Fact]
    public void TestCreateDirectoryAndFailure()
    {
        using var diskStream = new SparseMemoryStream();
        using var fs = FatFileSystem.FormatFloppy(diskStream, FloppyDiskType.HighDensity, "FLOPPY_IMG ");

        fs.CreateDirectory(Path.Combine("BAR", "BAZ", "QUX"));
        fs.CreateDirectory(Path.Combine("BAR", "BAZ", "QUX")); // Nothing is happening here
        fs.CreateDirectory("BAR");
        {
            using var file = fs.OpenFile(Path.Combine("BAR", "BAZ", "QUX", "TEST"), FileMode.Create);
            file.WriteByte(0);
        }

        Assert.Throws<IOException>(() => fs.CreateDirectory(Path.Combine("BAR", "BAZ", "QUX", "TEST")));
    }

    [Fact]
    public void TestLargeFileCreateOpenAppendTruncate()
    {
        using var diskStream = new SparseMemoryStream();
        using var fs = FatFileSystem.FormatFloppy(diskStream, FloppyDiskType.HighDensity, "FLOPPY_IMG ");

        var buffer = new byte[1024 * 1024];
        var rnd = new Random(0);
        rnd.NextBytes(buffer);
        using (var file = fs.OpenFile("TEST", FileMode.Create))
        {
            file.Write(buffer, 0, buffer.Length);
        }

        using (var file = fs.OpenFile("TEST", FileMode.Open))
        {
            var buffer2 = new byte[buffer.Length];
            int length = file.Read(buffer2, 0, buffer2.Length);
            Assert.Equal(length, buffer2.Length);

            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.Equal(buffer[i], buffer2[i]);
            }
        }

        using (var file = fs.OpenFile("TEST", FileMode.Append))
        {
            var smallerBuffer = new byte[] { 1, 2, 3, 4 };
            file.Write(smallerBuffer, 0, smallerBuffer.Length);
        }

        using (var file = fs.OpenFile("TEST", FileMode.Open))
        {
            var buffer2 = new byte[buffer.Length + 4];
            int length = file.Read(buffer2, 0, buffer2.Length);
            Assert.Equal(length, buffer2.Length);

            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.Equal(buffer[i], buffer2[i]);
            }

            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(i + 1, buffer2[buffer.Length + i]);
            }
        }

        using (var file = fs.OpenFile("TEST", FileMode.Truncate))
        {
            file.Write([0]);
        }

        var attr = fs.GetFileLength("TEST");
        Assert.Equal(1, attr);

        fs.DeleteFile("TEST");

        Assert.Throws<FileNotFoundException>(() => fs.GetFileLength("TEST"));

        using (var file = fs.OpenFile("ANOTHER", FileMode.Create))
        {
            file.Write(buffer, 0, buffer.Length);

            Assert.Equal(buffer.Length, file.Length);
        }

        Assert.Equal(buffer.Length, fs.PathToExtents("ANOTHER").Sum(ext => ext.Length));

        Assert.True(fs.FileExists("ANOTHER"));
    }

    [Fact]
    public void TestLargeFileCreateOpenAppendTruncate1KSectors()
    {
        const int sectorSize = 1024;
        var size = 20 * Sizes.OneMiB;
        var sectors = (int)(size / sectorSize);

        using var diskStream = new SparseMemoryStream();
        using var fs = FatFileSystem.FormatPartition(diskStream, "FLOPPY_IMG ", Geometry.FromCapacity(size, sectorSize), 0, sectors, 0);

        var buffer = new byte[1024 * 1024];
        var rnd = new Random(0);
        rnd.NextBytes(buffer);
        using (var file = fs.OpenFile("TEST", FileMode.Create))
        {
            file.Write(buffer, 0, buffer.Length);
        }

        using (var file = fs.OpenFile("TEST", FileMode.Open))
        {
            var buffer2 = new byte[buffer.Length];
            int length = file.Read(buffer2, 0, buffer2.Length);
            Assert.Equal(length, buffer2.Length);

            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.Equal(buffer[i], buffer2[i]);
            }
        }

        using (var file = fs.OpenFile("TEST", FileMode.Append))
        {
            var smallerBuffer = new byte[] { 1, 2, 3, 4 };
            file.Write(smallerBuffer, 0, smallerBuffer.Length);
        }

        using (var file = fs.OpenFile("TEST", FileMode.Open))
        {
            var buffer2 = new byte[buffer.Length + 4];
            int length = file.Read(buffer2, 0, buffer2.Length);
            Assert.Equal(length, buffer2.Length);

            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.Equal(buffer[i], buffer2[i]);
            }

            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(i + 1, buffer2[buffer.Length + i]);
            }
        }

        using (var file = fs.OpenFile("TEST", FileMode.Truncate))
        {
            file.Write([0]);
        }

        var attr = fs.GetFileLength("TEST");
        Assert.Equal(1, attr);

        fs.DeleteFile("TEST");

        Assert.Throws<FileNotFoundException>(() => fs.GetFileLength("TEST"));

        using (var file = fs.OpenFile("ANOTHER", FileMode.Create))
        {
            file.Write(buffer, 0, buffer.Length);

            Assert.Equal(buffer.Length, file.Length);
        }

        Assert.Equal(buffer.Length, fs.PathToExtents("ANOTHER").Sum(ext => ext.Length));

        Assert.True(fs.FileExists("ANOTHER"));
    }

    [Fact]
    public void TestShortName()
    {
        using var diskStream = new SparseMemoryStream();
        using var fs = FatFileSystem.FormatFloppy(diskStream, FloppyDiskType.HighDensity, "FLOPPY_IMG ");

        fs.CreateDirectory("A");
        fs.CreateDirectory("A.B");
        fs.CreateDirectory("a");
        fs.CreateDirectory("a.b");
        fs.CreateDirectory("a.B");
        fs.CreateDirectory("A1234567");
        fs.CreateDirectory("A1234567.ext");
        fs.CreateDirectory("this_is_a_long_name");
        fs.CreateDirectory("V1Abcd_this_is_to_long.TXT");
        fs.CreateDirectory("V2Abcd_this_is_to_long.TXT");
        fs.CreateDirectory("✨.txt");
        fs.CreateDirectory("abcdef🙂.txt");
        fs.CreateDirectory("abc🙂.txt");
        fs.CreateDirectory("ab🙂.txt");
        fs.CreateDirectory("c d.txt");
        fs.CreateDirectory("...txt");
        fs.CreateDirectory("..a.txt");
        fs.CreateDirectory("txt...");
        fs.CreateDirectory("a+b=c");
        fs.CreateDirectory("ab    .txt");
        fs.CreateDirectory("✨TAT");
        fs.CreateDirectory("a.b..c.d");
        fs.CreateDirectory("Mixed.Cas");
        fs.CreateDirectory("Mixed.txt");
        fs.CreateDirectory("mixed.Txt");

        Assert.Equal("A", fs.GetShortName("A"));
        Assert.Equal("A.B", fs.GetShortName("A.B"));
        Assert.Equal("A1234567", fs.GetShortName("A1234567"));
        Assert.Equal("A1234567.EXT", fs.GetShortName("A1234567.ext"));
        Assert.Equal("THIS_I~1", fs.GetShortName("this_is_a_long_name"));
        Assert.Equal("V1ABCD~1.TXT", fs.GetShortName("V1Abcd_this_is_to_long.TXT"));
        Assert.Equal("V2ABCD~1.TXT", fs.GetShortName("V2Abcd_this_is_to_long.TXT"));
        Assert.Equal("6393~1.TXT", fs.GetShortName("✨.txt"));
        Assert.Equal("ABCDEF~1.TXT", fs.GetShortName("abcdef🙂.txt"));
        Assert.Equal("ABC~1.TXT", fs.GetShortName("abc🙂.txt"));
        Assert.Equal("AB1F60~1.TXT", fs.GetShortName("ab🙂.txt"));

        // Force changing the short name
        fs.SetShortName("abcdef🙂.txt", "HELLO.TXT");
        Assert.Equal("HELLO.TXT", fs.GetShortName("abcdef🙂.txt"));

        // This should not be possible because the entry HELLO.TXT already exists
        Assert.Throws<IOException>(() => fs.SetShortName("abc🙂.txt", "HELLO.TXT"));
    }

    [Fact]
    public void TestFindPosition()
    {
        var pattern = "HELLO WORLD"u8;

        using var diskStream = new SparseMemoryStream();
        using var fs = FatFileSystem.FormatPartition(diskStream,
                                                     label: "PARTITION",
                                                     diskGeometry: Geometry.FromCapacity(20 << 20, 512),
                                                     firstSector: 0,
                                                     sectorCount: 20 << 11,
                                                     reservedSectors: 0);

        using (var file = fs.OpenFile("Test.txt", FileMode.Create, FileAccess.ReadWrite))
        {
            file.Write(pattern);
        }

        var firstExt = fs
            .PathToExtents("Test.txt")
            .FirstOrDefault();

        Assert.NotEqual(0, firstExt.Start);

        long? locationOnDisk;

        Span<byte> buffer = stackalloc byte[pattern.Length];

        using (var file = fs.OpenFile("Test.txt", FileMode.Open, FileAccess.Read))
        {
            buffer.Clear();
            file.ReadExactly(buffer);

            Assert.True(pattern.SequenceEqual(buffer));

            locationOnDisk = file.GetPositionInBaseStream(diskStream, 0);
        }

        Assert.NotNull(locationOnDisk);
        Assert.Equal(firstExt.Start, locationOnDisk.Value);

        diskStream.Position = locationOnDisk.Value;
        buffer.Clear();
        diskStream.ReadExactly(buffer);

        Assert.True(pattern.SequenceEqual(buffer));
    }

    [Fact]
    public void CreateDirectoryWithExistingData()
    {
        using var diskStream = new SparseMemoryStream();

        byte[] existingData = [
            0x00, 0x00, 0x00, 0x4E, 0x00, 0x0A, 0x7B, 0x9B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02,
            0x00, 0x0A, 0x7B, 0xE9, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x0A, 0x7B, 0xEB,
        ];

        for (var i = 0; i < 1474560 / existingData.Length; i++)
        {
            diskStream.Seek(i * existingData.Length, SeekOrigin.Begin);
            diskStream.Write(existingData, 0, existingData.Length);
        }

        diskStream.Position = 0;
        using var fs = FatFileSystem.FormatFloppy(diskStream, FloppyDiskType.HighDensity, "FLOPPY_IMG ");

        fs.CreateDirectory("dir");

        var entries = fs.GetFileSystemEntries("dir").ToList();

        Assert.Empty(entries);
    }

    [Fact]
    public void CreateFilesInSubdirectoryUsingFatFileSystemPerFile()
    {
        using var diskStream = new SparseMemoryStream();

        diskStream.Position = 0;
        using (var fsFormat = FatFileSystem.FormatFloppy(diskStream, FloppyDiskType.HighDensity, "FLOPPY_IMG "))
        {
            fsFormat.CreateDirectory("dir");            
        }

        for (var i = 0; i < 20; i++)
        {
            using var fsCreate = new FatFileSystem(diskStream);
            using var fileStream = fsCreate.OpenFile($"dir{Path.DirectorySeparatorChar}file{i}.txt", FileMode.Create);
            fileStream.Write(new byte[10]);
        }

        using var fsAssert = new FatFileSystem(diskStream);
        {
            var entries = fsAssert.GetFileSystemEntries("").ToList();
            Assert.Single(entries);
            Assert.Equal("dir", entries[0]);

            entries = fsAssert.GetFileSystemEntries("dir").ToList();
            Assert.Equal(20, entries.Count);
            Assert.Equal(Enumerable.Range(0, 20).Select(i => $"dir{Path.DirectorySeparatorChar}file{i}.txt"), entries);
        }
    }

    [Fact]
    public void CreateFilesInSubdirectoryUsingOneFatFileSystem()
    {
        using var diskStream = new SparseMemoryStream();

        diskStream.Position = 0;
        using (var fsFormat = FatFileSystem.FormatFloppy(diskStream, FloppyDiskType.HighDensity, "FLOPPY_IMG "))
        {
            fsFormat.CreateDirectory("dir");            
        }

        using (var fsCreate = new FatFileSystem(diskStream))
        {
            for (var i = 0; i < 20; i++)
            {
                using var fileStream = fsCreate.OpenFile($"dir{Path.DirectorySeparatorChar}file{i}.txt", FileMode.Create);
                fileStream.Write(new byte[10]);
            }            
        }

        using var fsAssert = new FatFileSystem(diskStream);
        {
            var entries = fsAssert.GetFileSystemEntries("").ToList();
            Assert.Single(entries);
            Assert.Equal("dir", entries[0]);

            entries = fsAssert.GetFileSystemEntries("dir").ToList();
            Assert.Equal(20, entries.Count);
            Assert.Equal(Enumerable.Range(0, 20).Select(i => $"dir{Path.DirectorySeparatorChar}file{i}.txt"), entries);
        }
    }
}