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
using Xunit;

namespace LibraryTests;

public class DiscFileSystemDirectoryTest
{
    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Create(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var dirInfo = fs.GetDirectoryInfo("SOMEDIR");
        dirInfo.Create();

        Assert.Single(fs.Root.GetDirectories());
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void CreateRecursive(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var sep = Path.DirectorySeparatorChar;

        var dirInfo = fs.GetDirectoryInfo($"SOMEDIR{sep}CHILDDIR");
        dirInfo.Create();

        Assert.Single(fs.Root.GetDirectories());
        Assert.Single(fs.GetDirectoryInfo(@"SOMEDIR").GetDirectories());
        Assert.Equal("CHILDDIR", fs.GetDirectoryInfo(@"SOMEDIR").GetDirectories().First().Name);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void CreateExisting(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var dirInfo = fs.GetDirectoryInfo("SOMEDIR");
        dirInfo.Create();
        dirInfo.Create();

        Assert.Single(fs.Root.GetDirectories());
    }

    [Theory]
    [Trait("Category", "ThrowsException")]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void CreateInvalid_Long(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var dirInfo = fs.GetDirectoryInfo(new String('X', 256));
        Assert.Throws<IOException>(dirInfo.Create);
    }

    [Theory]
    [Trait("Category", "ThrowsException")]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void CreateInvalid_Characters(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var dirInfo = fs.GetDirectoryInfo("SOME\0DIR");
        Assert.Throws<IOException>(() => dirInfo.Create());
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Exists(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var dirInfo = fs.GetDirectoryInfo(@"SOMEDIR\CHILDDIR");
        dirInfo.Create();

        var sep = Path.DirectorySeparatorChar;

        Assert.True(fs.GetDirectoryInfo($"{sep}").Exists);
        Assert.True(fs.GetDirectoryInfo($"SOMEDIR").Exists);
        Assert.True(fs.GetDirectoryInfo($"SOMEDIR{sep}CHILDDIR").Exists);
        Assert.True(fs.GetDirectoryInfo($"SOMEDIR{sep}CHILDDIR{sep}").Exists);
        Assert.False(fs.GetDirectoryInfo($"NONDIR").Exists);
        Assert.False(fs.GetDirectoryInfo($"SOMEDIR{sep}NONDIR").Exists);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void FullName(NewFileSystemDelegate fsFactory)
    {
        var sep = Path.DirectorySeparatorChar;

        var fs = fsFactory();

        Assert.Equal($"{sep}", fs.Root.FullName);
        Assert.Equal($"SOMEDIR{sep}", fs.GetDirectoryInfo($"SOMEDIR").FullName);
        Assert.Equal($"SOMEDIR{sep}CHILDDIR{sep}", fs.GetDirectoryInfo($"SOMEDIR{sep}CHILDDIR").FullName);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Delete(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        fs.CreateDirectory(@"Fred");
        Assert.Single(fs.Root.GetDirectories());

        fs.Root.GetDirectories(@"Fred").First().Delete();
        Assert.Empty(fs.Root.GetDirectories());
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void DeleteRecursive(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var sep = Path.DirectorySeparatorChar;

        fs.CreateDirectory($"Fred{sep}child");
        Assert.Single(fs.Root.GetDirectories());

        fs.Root.GetDirectories($"Fred").First().Delete(true);
        Assert.Empty(fs.Root.GetDirectories());
    }

    [Theory]
    [Trait("Category", "ThrowsException")]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void DeleteRoot(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        Assert.Throws<IOException>(() => fs.Root.Delete());
    }

    [Theory]
    [Trait("Category", "ThrowsException")]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void DeleteNonEmpty_NonRecursive(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        fs.CreateDirectory(@"Fred\child");
        Assert.Throws<IOException>(() => fs.Root.GetDirectories(@"Fred").First().Delete());
    }

    [Theory]
    [Trait("Category", "SlowTest")]
    [MemberData(nameof(FileSystemSource.QuickReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void CreateDeleteLeakTest(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        for (var i = 0; i < 2000; ++i)
        {
            fs.CreateDirectory(@"Fred");
            fs.Root.GetDirectories(@"Fred").First().Delete();
        }

        fs.CreateDirectory(@"SOMEDIR");
        var dirInfo = fs.GetDirectoryInfo(@"SOMEDIR");
        Assert.NotNull(dirInfo);

        var sep = Path.DirectorySeparatorChar;

        for (var i = 0; i < 2000; ++i)
        {
            fs.CreateDirectory($"SOMEDIR{sep}Fred");
            dirInfo.GetDirectories($"Fred").First().Delete();
        }
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Move(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var sep = Path.DirectorySeparatorChar;

        fs.CreateDirectory($"SOMEDIR{sep}CHILD{sep}GCHILD");
        fs.GetDirectoryInfo($"SOMEDIR{sep}CHILD").MoveTo("NEWDIR");

        Assert.Equal(2, fs.Root.GetDirectories().Count());
        Assert.Empty(fs.Root.GetDirectories("SOMEDIR").First().GetDirectories());
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Extension(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        Assert.Equal("dir", fs.GetDirectoryInfo("fred.dir").Extension);
        Assert.Equal("", fs.GetDirectoryInfo("fred").Extension);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void GetDirectories(NewFileSystemDelegate fsFactory)
    {
        var sep = Path.DirectorySeparatorChar;

        var fs = fsFactory();

        fs.CreateDirectory($"SOMEDIR{sep}CHILD{sep}GCHILD");
        fs.CreateDirectory($"A.DIR");

        Assert.Equal(2, fs.Root.GetDirectories().Count());

        var someDir = fs.Root.GetDirectories($"SoMeDir").First();
        Assert.Single(fs.Root.GetDirectories("SOMEDIR"));
        Assert.Equal("SOMEDIR", someDir.Name);

        Assert.Single(someDir.GetDirectories("*.*"));
        Assert.Equal("CHILD", someDir.GetDirectories("*.*").First().Name);
        Assert.Equal(2, someDir.GetDirectories("*.*", SearchOption.AllDirectories).Count());

        Assert.Equal(4, fs.Root.GetDirectories("*.*", SearchOption.AllDirectories).Count());
        Assert.Equal(2, fs.Root.GetDirectories("*.*", SearchOption.TopDirectoryOnly).Count());

        Assert.Single(fs.Root.GetDirectories("*.DIR", SearchOption.AllDirectories));
        Assert.Equal($"A.DIR{sep}", fs.Root.GetDirectories("*.DIR", SearchOption.AllDirectories).First().FullName);

        Assert.Single(fs.Root.GetDirectories("GCHILD", SearchOption.AllDirectories));
        Assert.Equal($"SOMEDIR{sep}CHILD{sep}GCHILD{sep}", fs.Root.GetDirectories("GCHILD", SearchOption.AllDirectories).First().FullName);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void GetDirectories_BadPath(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        Assert.Throws<DirectoryNotFoundException>(() => fs.GetDirectories(@"\baddir").Any());
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void GetFiles(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var sep = Path.DirectorySeparatorChar;

        fs.CreateDirectory($"SOMEDIR{sep}CHILD{sep}GCHILD");
        fs.CreateDirectory($"AAA.DIR");
        using (var s = fs.OpenFile($"FOO.TXT", FileMode.Create))
        {
        }

        using (var s = fs.OpenFile($"SOMEDIR{sep}CHILD.TXT", FileMode.Create))
        {
        }

        using (var s = fs.OpenFile($"SOMEDIR{sep}FOO.TXT", FileMode.Create))
        {
        }

        using (var s = fs.OpenFile($"SOMEDIR{sep}CHILD{sep}GCHILD{sep}BAR.TXT", FileMode.Create))
        {
        }

        Assert.Single(fs.Root.GetFiles());
        Assert.Equal("FOO.TXT", fs.Root.GetFiles().First().FullName);

        Assert.Equal(2, fs.Root.GetDirectories("SOMEDIR").First().GetFiles("*.TXT").Count());
        Assert.Equal(4, fs.Root.GetFiles("*.TXT", SearchOption.AllDirectories).Count());

        Assert.Empty(fs.Root.GetFiles("*.DIR", SearchOption.AllDirectories));
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void GetFileSystemInfos(NewFileSystemDelegate fsFactory)
    {
        var sep = Path.DirectorySeparatorChar;

        var fs = fsFactory();

        fs.CreateDirectory($"SOMEDIR{sep}CHILD{sep}GCHILD");
        fs.CreateDirectory($"AAA.EXT");
        using (var s = fs.OpenFile($"FOO.TXT", FileMode.Create))
        {
        }

        using (var s = fs.OpenFile($"SOMEDIR{sep}CHILD.EXT", FileMode.Create))
        {
        }

        using (var s = fs.OpenFile($"SOMEDIR{sep}FOO.TXT", FileMode.Create))
        {
        }

        using (var s = fs.OpenFile($"SOMEDIR{sep}CHILD{sep}GCHILD{sep}BAR.TXT", FileMode.Create))
        {
        }

        Assert.Equal(3, fs.Root.GetFileSystemInfos().Count());

        Assert.Single(fs.Root.GetFileSystemInfos("*.EXT"));
        Assert.Equal(2, fs.Root.GetFileSystemInfos("*.?XT").Count());
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Parent(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        fs.CreateDirectory("SOMEDIR");

        Assert.Equal(fs.Root, fs.Root.GetDirectories("SOMEDIR").First().Parent);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void Parent_Root(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        Assert.Null(fs.Root.Parent);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void CreationTimeUtc(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        fs.CreateDirectory("DIR");

        Assert.True(DateTime.UtcNow >= fs.Root.GetDirectories("DIR").First().CreationTimeUtc);
        Assert.True(DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(10)) <= fs.Root.GetDirectories("DIR").First().CreationTimeUtc);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void CreationTime(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        fs.CreateDirectory("DIR");

        Assert.True(DateTime.Now >= fs.Root.GetDirectories("DIR").First().CreationTime);
        Assert.True(DateTime.Now.Subtract(TimeSpan.FromSeconds(10)) <= fs.Root.GetDirectories("DIR").First().CreationTime);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void LastAccessTime(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var sep = Path.DirectorySeparatorChar;

        fs.CreateDirectory("DIR");
        var di = fs.GetDirectoryInfo("DIR");

        var baseTime = DateTime.Now - TimeSpan.FromDays(2);
        di.LastAccessTime = baseTime;

        fs.CreateDirectory($"DIR{sep}CHILD");

        di = fs.GetDirectoryInfo("DIR");

        Assert.True(baseTime < di.LastAccessTime);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void LastWriteTime(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        var sep = Path.DirectorySeparatorChar;

        fs.CreateDirectory("DIR");
        var di = fs.GetDirectoryInfo("DIR");

        var baseTime = DateTime.Now - TimeSpan.FromMinutes(10);
        di.LastWriteTime = baseTime;

        fs.CreateDirectory($"DIR{sep}CHILD");

        di = fs.GetDirectoryInfo("DIR");

        Assert.True(baseTime < di.LastWriteTime);
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void DirectoryInfo_Equals(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        Assert.Equal(fs.GetDirectoryInfo("foo"), fs.GetDirectoryInfo("foo"));
    }

    [Theory]
    [MemberData(nameof(FileSystemSource.ReadWriteFileSystems), MemberType = typeof(FileSystemSource))]
    public void RootBehaviour(NewFileSystemDelegate fsFactory)
    {
        var fs = fsFactory();

        // Not all file systems can modify the root directory, so we just make sure 'get' and 'no-op' change work.
        fs.Root.Attributes = fs.Root.Attributes;
        fs.Root.CreationTimeUtc = fs.Root.CreationTimeUtc;
        fs.Root.LastAccessTimeUtc = fs.Root.LastAccessTimeUtc;
        fs.Root.LastWriteTimeUtc = fs.Root.LastWriteTimeUtc;
    }
}
