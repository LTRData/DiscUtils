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
using Xunit;

namespace LibraryTests;

public class DiscFileSystemFileTest
{
    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void CreateFile(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        using (var s = fs.GetFileInfo("foo.txt").Open(FileMode.Create, FileAccess.ReadWrite))
        {
            s.WriteByte(1);
        }

        var fi = fs.GetFileInfo("foo.txt");

        Assert.True(fi.Exists);
        Assert.Equal(FileAttributes.Archive, fi.Attributes);
        Assert.Equal(1, fi.Length);

        using (var s = fs.OpenFile("Foo.txt", FileMode.Open, FileAccess.Read))
        {
            Assert.Equal(1, s.ReadByte());
        }
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    [Trait("Category", "ThrowsException")]
    public void CreateFileInvalid_Long(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        Assert.Throws<IOException>(() =>
        {
            using var s = fs.GetFileInfo(new string('X', 256)).Open(FileMode.Create, FileAccess.ReadWrite);
            s.WriteByte(1);
        });
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    [Trait("Category", "ThrowsException")]
    public void CreateFileInvalid_Characters(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        Assert.Throws<IOException>(() =>
        {
            using var s = fs.GetFileInfo("A\0File").Open(FileMode.Create, FileAccess.ReadWrite);
            s.WriteByte(1);
        });
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void DeleteFile(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        using (var s = fs.GetFileInfo("foo.txt").Open(FileMode.Create, FileAccess.ReadWrite))
        {
        }

        Assert.Single(fs.Root.GetFiles());

        var fi = fs.GetFileInfo("foo.txt");

        fi.Delete();

        Assert.Empty(fs.Root.GetFiles());
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Length(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        using (var s = fs.GetFileInfo("foo.txt").Open(FileMode.Create, FileAccess.ReadWrite))
        {
            s.SetLength(3128);
        }

        Assert.Equal(3128, fs.GetFileInfo("foo.txt").Length);

        using (var s = fs.OpenFile("foo.txt", FileMode.Open, FileAccess.ReadWrite))
        {
            s.SetLength(3);
            Assert.Equal(3, s.Length);
        }

        Assert.Equal(3, fs.GetFileInfo("foo.txt").Length);

        using (var s = fs.OpenFile("foo.txt", FileMode.Open, FileAccess.ReadWrite))
        {
            s.SetLength(3333);

            var buffer = new byte[512];
            for (var i = 0; i < buffer.Length; ++i)
            {
                buffer[i] = (byte)i;
            }

            s.Write(buffer, 0, buffer.Length);
            s.Write(buffer, 0, buffer.Length);

            Assert.Equal(1024, s.Position);
            Assert.Equal(3333, s.Length);

            s.SetLength(512);

            Assert.Equal(512, s.Length);
        }

        using (var s = fs.OpenFile("foo.txt", FileMode.Open, FileAccess.ReadWrite))
        {
            var buffer = new byte[512];
            var numRead = s.Read(buffer, 0, buffer.Length);
            var totalRead = 0;
            while (numRead != 0)
            {
                totalRead += numRead;
                numRead = s.Read(buffer, totalRead, buffer.Length - totalRead);
            }

            for (var i = 0; i < buffer.Length; ++i)
            {
                Assert.Equal((byte)i, buffer[i]);
            }
        }
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    [Trait("Category", "ThrowsException")]
    public void Open_FileNotFound(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var di = fs.GetFileInfo("foo.txt");

        Assert.Throws<FileNotFoundException>(() =>
        {
            using var s = di.Open(FileMode.Open);
        });
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    [Trait("Category", "ThrowsException")]
    public void Open_FileExists(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var di = fs.GetFileInfo("foo.txt");
        using (var s = di.Open(FileMode.Create))
        {
            s.WriteByte(1);
        }

        Assert.Throws<IOException>(() =>
        {
            using var s = di.Open(FileMode.CreateNew);
        });
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    [Trait("Category", "ThrowsException")]
    public void Open_DirExists(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        fs.CreateDirectory("FOO.TXT");

        var di = fs.GetFileInfo("foo.txt");
        Assert.Throws<IOException>(() => di.Open(FileMode.Create));
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Open_Read(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var di = fs.GetFileInfo("foo.txt");

        using (var s = di.Open(FileMode.Create))
        {
            s.WriteByte(1);
        }

        using (var s = di.Open(FileMode.Open, FileAccess.Read))
        {
            Assert.False(s.CanWrite);
            Assert.True(s.CanRead);

            Assert.Equal(1, s.ReadByte());
        }
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    [Trait("Category", "ThrowsException")]
    public void Open_Read_Fail(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var di = fs.GetFileInfo("foo.txt");
        using var s = di.Open(FileMode.Create, FileAccess.Read);
        Assert.Throws<IOException>(() => s.WriteByte(1));
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Open_Write(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var di = fs.GetFileInfo("foo.txt");
        using var s = di.Open(FileMode.Create, FileAccess.Write);
        Assert.True(s.CanWrite);
        Assert.False(s.CanRead);
        s.WriteByte(1);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    [Trait("Category", "ThrowsException")]
    public void Open_Write_Fail(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var di = fs.GetFileInfo("foo.txt");
        using (var s = di.Open(FileMode.Create, FileAccess.ReadWrite))
        {
            s.WriteByte(1);
        }

        using (var s = di.Open(FileMode.Open, FileAccess.Write))
        {
            Assert.True(s.CanWrite);
            Assert.False(s.CanRead);

            Assert.Throws<IOException>(() => s.ReadByte());
        }
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Name(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var sep = Path.DirectorySeparatorChar;

        Assert.Equal("foo.txt", fs.GetFileInfo("foo.txt").Name);
        Assert.Equal("foo.txt", fs.GetFileInfo($"path{sep}foo.txt").Name);
        Assert.Equal("foo.txt", fs.GetFileInfo($"{sep}foo.txt").Name);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Attributes(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var fi = fs.GetFileInfo("foo.txt");
        using (var s = fi.Open(FileMode.Create))
        {
        }

        // Check default attributes
        Assert.Equal(FileAttributes.Archive, fi.Attributes);

        // Check round-trip
        var newAttrs = FileAttributes.Hidden | FileAttributes.ReadOnly | FileAttributes.System;
        fi.Attributes = newAttrs;
        Assert.Equal(newAttrs, fi.Attributes);

        // And check persistence to disk
        Assert.Equal(newAttrs, fs.GetFileInfo("foo.txt").Attributes);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    [Trait("Category", "ThrowsException")]
    public void Attributes_ChangeType(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var fi = fs.GetFileInfo("foo.txt");
        using (var s = fi.Open(FileMode.Create))
        {
        }

        Assert.Throws<ArgumentException>(() => fi.Attributes = fi.Attributes | FileAttributes.Directory);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Exists(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var fi = fs.GetFileInfo("foo.txt");

        Assert.False(fi.Exists);

        using (var s = fi.Open(FileMode.Create))
        {
        }

        Assert.True(fi.Exists);

        fs.CreateDirectory("dir.txt");
        Assert.False(fs.GetFileInfo("dir.txt").Exists);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void CreationTimeUtc(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        using (var s = fs.OpenFile("foo.txt", FileMode.Create))
        {
        }

        Assert.True(DateTime.UtcNow >= fs.GetFileInfo("foo.txt").CreationTimeUtc);
        Assert.True(DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(10)) <= fs.GetFileInfo("foo.txt").CreationTimeUtc);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void CreationTime(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        using (var s = fs.OpenFile("foo.txt", FileMode.Create))
        {
        }

        Assert.True(DateTime.Now >= fs.GetFileInfo("foo.txt").CreationTime);
        Assert.True(DateTime.Now.Subtract(TimeSpan.FromSeconds(10)) <= fs.GetFileInfo("foo.txt").CreationTime);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void LastAccessTime(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        using (var s = fs.OpenFile("foo.txt", FileMode.Create))
        {
        }

        var fi = fs.GetFileInfo("foo.txt");

        var baseTime = DateTime.Now - TimeSpan.FromDays(2);
        fi.LastAccessTime = baseTime;

        using (var s = fs.OpenFile("foo.txt", FileMode.Open, FileAccess.Read))
        {
        }

        fi = fs.GetFileInfo("foo.txt");

        Assert.True(baseTime < fi.LastAccessTime);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void LastWriteTime(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        using (var s = fs.OpenFile("foo.txt", FileMode.Create))
        {
        }

        var fi = fs.GetFileInfo("foo.txt");

        var baseTime = DateTime.Now - TimeSpan.FromMinutes(10);
        fi.LastWriteTime = baseTime;

        using (var s = fs.OpenFile("foo.txt", FileMode.Open))
        {
            s.WriteByte(1);
        }

        fi = fs.GetFileInfo("foo.txt");

        Assert.True(baseTime < fi.LastWriteTime);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Delete(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        using (var s = fs.OpenFile("foo.txt", FileMode.Create))
        {
        }

        fs.GetFileInfo("foo.txt").Delete();

        Assert.False(fs.FileExists("foo.txt"));
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    [Trait("Category", "ThrowsException")]
    public void Delete_Dir(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        fs.CreateDirectory("foo.txt");

        Assert.Throws<FileNotFoundException>(() => fs.GetFileInfo("foo.txt").Delete());
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    [Trait("Category", "ThrowsException")]
    public void Delete_NoFile(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        Assert.Throws<FileNotFoundException>(() => fs.GetFileInfo("foo.txt").Delete());
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void CopyFile(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var fi = fs.GetFileInfo("foo.txt");

        using (var s = fi.Create())
        {
            for (var i = 0; i < 10; ++i)
            {
                s.Write(new byte[111], 0, 111);
            }
        }

        fi.Attributes = FileAttributes.Hidden | FileAttributes.System;

        fi.CopyTo("foo2.txt");

        fi = fs.GetFileInfo("foo2.txt");
        Assert.True(fi.Exists);
        Assert.Equal(1110, fi.Length);
        Assert.Equal(FileAttributes.Hidden | FileAttributes.System, fi.Attributes);

        fi = fs.GetFileInfo("foo.txt");
        Assert.True(fi.Exists);

        fi = fs.GetFileInfo("foo2.txt");
        Assert.True(fi.Exists);
        Assert.Equal(1110, fi.Length);
        Assert.Equal(FileAttributes.Hidden | FileAttributes.System, fi.Attributes);

        fi = fs.GetFileInfo("foo.txt");
        Assert.True(fi.Exists);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void MoveFile(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var fi = fs.GetFileInfo("foo.txt");

        using (var s = fi.Create())
        {
            for (var i = 0; i < 10; ++i)
            {
                s.Write(new byte[111], 0, 111);
            }
        }

        fi.Attributes = FileAttributes.Hidden | FileAttributes.System;

        fi.MoveTo("foo2.txt");

        fi = fs.GetFileInfo("foo2.txt");
        Assert.True(fi.Exists);
        Assert.Equal(1110, fi.Length);
        Assert.Equal(FileAttributes.Hidden | FileAttributes.System, fi.Attributes);

        fi = fs.GetFileInfo("foo.txt");
        Assert.False(fi.Exists);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void MoveFile_Overwrite(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var fi = fs.GetFileInfo("foo.txt");
        using (var s = fi.Create())
        {
            s.WriteByte(1);
        }

        var fi2 = fs.GetFileInfo("foo2.txt");
        using (var s = fi2.Create())
        {
        }

        fs.MoveFile("foo.txt", "foo2.txt", true);

        Assert.False(fi.Exists);
        Assert.True(fi2.Exists);
        Assert.Equal(1, fi2.Length);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void FileInfo_Equals(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        Assert.Equal(fs.GetFileInfo("foo.txt"), fs.GetFileInfo("foo.txt"));
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Parent(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var sep = Path.DirectorySeparatorChar;

        fs.CreateDirectory($"SOMEDIR{sep}ADIR");
        using (var s = fs.OpenFile($"SOMEDIR{sep}ADIR{sep}FILE.TXT", FileMode.Create))
        {
        }

        var fi = fs.GetFileInfo($"SOMEDIR{sep}ADIR{sep}FILE.TXT");
        Assert.Equal(fs.GetDirectoryInfo($"SOMEDIR{sep}ADIR"), fi.Parent);
        Assert.Equal(fs.GetDirectoryInfo($"SOMEDIR{sep}ADIR"), fi.Directory);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void VolumeLabel(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var volLabel = fs.VolumeLabel;
        Assert.NotNull(volLabel);
    }
}
