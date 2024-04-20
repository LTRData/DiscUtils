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

using System.Collections.Generic;
using System.IO;
using DiscUtils;
using DiscUtils.Complete;
using DiscUtils.Vmdk;
using Xunit;

namespace LibraryTests.Vmdk;

public class DiskTest
{
    public DiskTest()
    {
        SetupHelper.SetupComplete();
    }

    [Fact]
    public void InitializeFixed()
    {
        using var disk = Disk.Initialize(new InMemoryFileSystem(), "a.vmdk", 8 * 1024 * 1024, DiskCreateType.MonolithicFlat);
        Assert.NotNull(disk);
        Assert.True(disk.Geometry.Value.Capacity is > (long)(7.9 * 1024 * 1024) and < (long)(8.1 * 1024 * 1024));
        Assert.True(disk.Geometry.Value.Capacity == disk.Content.Length);

        var links = new List<DiskImageFile>(disk.Links);
        var paths = new List<string>(links[0].ExtentPaths);
        Assert.Single(paths);
        Assert.Equal("a-flat.vmdk", paths[0]);
    }

    [Fact]
    public void InitializeFixedIDE()
    {
        using var disk = Disk.Initialize(new InMemoryFileSystem(), "a.vmdk", 8 * 1024 * 1024, DiskCreateType.MonolithicFlat, DiskAdapterType.Ide);
        Assert.NotNull(disk);
        Assert.True(disk.Geometry.Value.Capacity is > (long)(7.9 * 1024 * 1024) and < (long)(8.1 * 1024 * 1024));
        Assert.True(disk.Geometry.Value.Capacity == disk.Content.Length);

        var links = new List<DiskImageFile>(disk.Links);
        var paths = new List<string>(links[0].ExtentPaths);
        Assert.Single(paths);
        Assert.Equal("a-flat.vmdk", paths[0]);
    }

    [Fact]
    public void InitializeDynamic()
    {
        DiscFileSystem fs = new InMemoryFileSystem();
        using (var disk = Disk.Initialize(fs, "a.vmdk", 16 * 1024L * 1024 * 1024, DiskCreateType.MonolithicSparse))
        {
            Assert.NotNull(disk);
            Assert.True(disk.Geometry.Value.Capacity is > (long)(15.8 * 1024L * 1024 * 1024) and <= (16 * 1024L * 1024 * 1024));
            Assert.True(disk.Content.Length == 16 * 1024L * 1024 * 1024);
        }

        Assert.True(fs.GetFileLength("a.vmdk") > 2 * 1024 * 1024);
        Assert.True(fs.GetFileLength("a.vmdk") < 4 * 1024 * 1024);

        using (var disk = new Disk(fs, "a.vmdk", FileAccess.Read))
        {
            Assert.True(disk.Geometry.Value.Capacity is > (long)(15.8 * 1024L * 1024 * 1024) and <= (16 * 1024L * 1024 * 1024));
            Assert.True(disk.Content.Length == 16 * 1024L * 1024 * 1024);

            var links = new List<DiskImageFile>(disk.Links);
            var paths = new List<string>(links[0].ExtentPaths);
            Assert.Single(paths);
            Assert.Equal("a.vmdk", paths[0]);
        }
    }

    [Fact]
    public void InitializeDifferencing()
    {
        var sep = Path.DirectorySeparatorChar;

        DiscFileSystem fs = new InMemoryFileSystem();

        var baseFile = DiskImageFile.Initialize(fs, $"{sep}base{sep}base.vmdk", 16 * 1024L * 1024 * 1024, DiskCreateType.MonolithicSparse);
        using (var disk = Disk.InitializeDifferencing(fs, $"{sep}diff{sep}diff.vmdk", DiskCreateType.MonolithicSparse, $"{sep}base{sep}base.vmdk"))
        {
            Assert.NotNull(disk);
            Assert.True(disk.Geometry.Value.Capacity is > (long)(15.8 * 1024L * 1024 * 1024) and < (16 * 1024L * 1024 * 1024));
            Assert.True(disk.Content.Length == 16 * 1024L * 1024 * 1024);
            Assert.Equal(2, new List<VirtualDiskLayer>(disk.Layers).Count);

            var links = new List<DiskImageFile>(disk.Links);
            Assert.Equal(2, links.Count);

            var paths = new List<string>(links[0].ExtentPaths);
            Assert.Single(paths);
            Assert.Equal("diff.vmdk", paths[0]);
        }

        Assert.True(fs.GetFileLength($"{sep}diff{sep}diff.vmdk") > 2 * 1024 * 1024);
        Assert.True(fs.GetFileLength($"{sep}diff{sep}diff.vmdk") < 4 * 1024 * 1024);
    }

    [Fact]
    public void InitializeDifferencingRelPath()
    {
        DiscFileSystem fs = new InMemoryFileSystem();

        var sep = Path.DirectorySeparatorChar;

        var baseFile = DiskImageFile.Initialize(fs, $"{sep}dir{sep}subdir{sep}base.vmdk", 16 * 1024L * 1024 * 1024, DiskCreateType.MonolithicSparse);
        using (var disk = Disk.InitializeDifferencing(fs, $"{sep}dir{sep}diff.vmdk", DiskCreateType.MonolithicSparse, $"subdir{sep}base.vmdk"))
        {
            Assert.NotNull(disk);
            Assert.True(disk.Geometry.Value.Capacity is > (long)(15.8 * 1024L * 1024 * 1024) and < (16 * 1024L * 1024 * 1024));
            Assert.True(disk.Content.Length == 16 * 1024L * 1024 * 1024);
            Assert.Equal(2, new List<VirtualDiskLayer>(disk.Layers).Count);
        }

        Assert.True(fs.GetFileLength($"{sep}dir{sep}diff.vmdk") > 2 * 1024 * 1024);
        Assert.True(fs.GetFileLength($"{sep}dir{sep}diff.vmdk") < 4 * 1024 * 1024);
    }

    [Fact]
    public void ReadOnlyHosted()
    {
        DiscFileSystem fs = new InMemoryFileSystem();
        using (var disk = Disk.Initialize(fs, "a.vmdk", 16 * 1024L * 1024 * 1024, DiskCreateType.MonolithicSparse))
        {
        }

        var d2 = new Disk(fs, "a.vmdk", FileAccess.Read);
        Assert.False(d2.Content.CanWrite);
    }
}
