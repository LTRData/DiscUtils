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
using DiscUtils.Iso9660;
using DiscUtils.Streams;
using Xunit;

namespace LibraryTests.Iso9660;

public class IsoFileSystemTest
{
    [Fact]
    public void CanWrite()
    {
        var builder = new CDBuilder();
        var fs = new CDReader(builder.Build(), false);
        Assert.False(fs.CanWrite);
    }

    [Fact]
    public void FileInfo()
    {
        var builder = new CDBuilder();
        var fs = new CDReader(builder.Build(), false);
        var fi = fs.GetFileInfo(@"SOMEDIR\SOMEFILE.TXT");
        Assert.NotNull(fi);
    }

    [Fact]
    public void DirectoryInfo()
    {
        var builder = new CDBuilder();
        var fs = new CDReader(builder.Build(), false);
        var fi = fs.GetDirectoryInfo(@"SOMEDIR");
        Assert.NotNull(fi);
    }

    [Fact]
    public void FileSystemInfo()
    {
        var builder = new CDBuilder();
        var fs = new CDReader(builder.Build(), false);
        var fi = fs.GetFileSystemInfo(@"SOMEDIR\SOMEFILE");
        Assert.NotNull(fi);
    }

    [Fact]
    public void Root()
    {
        var builder = new CDBuilder();
        var fs = new CDReader(builder.Build(), false);
        Assert.NotNull(fs.Root);
        Assert.True(fs.Root.Exists);
        Assert.Empty(fs.Root.Name);
        Assert.Null(fs.Root.Parent);
    }

    [Fact]
    public void Dirs()
    {
        var dir1 = Path.DirectorySeparatorChar + "DIR1";
        var dir2 = Path.DirectorySeparatorChar + "DIR2";

        var builder = new CDBuilder();
        builder.AddDirectory(dir1);
        builder.AddDirectory(dir2);
        var fs = new CDReader(builder.Build(), true);
        Assert.Equal(fs.GetDirectories(""), [dir1, dir2]);
    }

    [Fact]
    public void LargeDirectory()
    {
        var builder = new CDBuilder
        {
            UseJoliet = true
        };

        for (var i = 0; i < 3000; ++i)
        {
            builder.AddFile($"FILE{i}.TXT", Array.Empty<byte>());
        }

        var reader = new CDReader(builder.Build(), true);

        Assert.Equal(3000, reader.Root.GetFiles().Count());
    }

    [Fact]
    public void HideVersions()
    {
        var builder = new CDBuilder
        {
            UseJoliet = true
        };
        builder.AddFile("FILE.TXT;1", Array.Empty<byte>());

        var ms = new MemoryStream();
        SparseStream.Pump(builder.Build(), ms);

        var sep = Path.DirectorySeparatorChar;

        var reader = new CDReader(ms, true, false);
        Assert.Equal($"{sep}FILE.TXT;1", reader.GetFiles("").First());
        Assert.Equal($"{sep}FILE.TXT;1", reader.GetFileSystemEntries("").First());

        reader = new CDReader(ms, true, true);
        Assert.Equal($"{sep}FILE.TXT", reader.GetFiles("").First());
        Assert.Equal($"{sep}FILE.TXT", reader.GetFileSystemEntries("").First());
    }
}
