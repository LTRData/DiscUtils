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
using System.Collections.Generic;
using System.IO;
using DiscUtils;
using DiscUtils.Streams;
using DiscUtils.Vhdx;
using Xunit;

namespace LibraryTests.Vhdx;

public class DiskTest
{
    [Fact]
    public void InitializeFixed()
    {
        var ms = new MemoryStream();
        using (var disk = Disk.InitializeDynamic(ms, Ownership.None, 8 * Sizes.OneMiB))
        {
            Assert.NotNull(disk);
            Assert.True(disk.Geometry.Value.Capacity is > (long)(7.5 * Sizes.OneMiB) and <= (8 * Sizes.OneMiB));
        }

        // Check the stream is still valid
        ms.ReadByte();
        ms.Dispose();
    }

    [Fact]
    public void InitializeFixedOwnStream()
    {
        var ms = new MemoryStream();
        using (var disk = Disk.InitializeFixed(ms, Ownership.Dispose, 8 * Sizes.OneMiB))
        {
        }

        Assert.Throws<ObjectDisposedException>(() => ms.ReadByte());
    }


    [Fact]
    public void InitializeDynamicOwnStream()
    {
        var ms = new MemoryStream();
        using (var disk = Disk.InitializeDynamic(ms, Ownership.Dispose, 8 * Sizes.OneMiB))
        {
        }

        Assert.Throws<ObjectDisposedException>(() => ms.ReadByte());
    }

    [Fact]
    public void InitializeDynamic()
    {
        var ms = new MemoryStream();
        using (var disk = Disk.InitializeDynamic(ms, Ownership.None, 16 * Sizes.OneGiB))
        {
            Assert.NotNull(disk);
            Assert.True(disk.Geometry.Value.Capacity is > (long)(15.8 * Sizes.OneGiB) and <= (16 * Sizes.OneGiB));
        }

        Assert.True(8 * Sizes.OneMiB > ms.Length);

        using (var disk = new Disk(ms, Ownership.Dispose))
        {
            Assert.True(disk.Geometry.Value.Capacity is > (long)(15.8 * Sizes.OneGiB) and <= (16 * Sizes.OneGiB));
        }
    }

    [Fact]
    public void InitializeDifferencing()
    {
        var baseStream = new MemoryStream();
        var diffStream = new MemoryStream();
        var baseFile = DiskImageFile.InitializeDynamic(baseStream, Ownership.Dispose, 16 * Sizes.OneGiB);
        using (var disk = Disk.InitializeDifferencing(diffStream, Ownership.None, baseFile, Ownership.Dispose, @"C:\TEMP\Base.vhd", @".\Base.vhd", DateTime.UtcNow))
        {
            Assert.NotNull(disk);
            Assert.True(disk.Geometry.Value.Capacity is > (long)(15.8 * Sizes.OneGiB) and <= (16 * Sizes.OneGiB));
            Assert.True(disk.Geometry.Value.Capacity == baseFile.Geometry.Capacity);
            Assert.Equal(2, new List<VirtualDiskLayer>(disk.Layers).Count);
        }

        Assert.True(8 * Sizes.OneMiB > diffStream.Length);
        diffStream.Dispose();
    }

    [Fact]
    public void ConstructorDynamic()
    {
        Geometry geometry;
        var ms = new MemoryStream();
        using (var disk = Disk.InitializeDynamic(ms, Ownership.None, 16 * Sizes.OneGiB))
        {
            geometry = disk.Geometry.Value;
        }

        using (var disk = new Disk(ms, Ownership.None))
        {
            Assert.Equal(geometry, disk.Geometry);
            Assert.NotNull(disk.Content);
        }

        using (var disk = new Disk(ms, Ownership.Dispose))
        {
            Assert.Equal(geometry, disk.Geometry);
            Assert.NotNull(disk.Content);
        }
    }

    [Fact]
    public void ConstructorFixed()
    {
        Geometry geometry;
        var ms = new MemoryStream();
        using (var disk = Disk.InitializeFixed(ms, Ownership.None, 16 * Sizes.OneMiB))
        {
            geometry = disk.Geometry.Value;
        }

        using (var disk = new Disk(ms, Ownership.None))
        {
            Assert.Equal(geometry, disk.Geometry);
            Assert.NotNull(disk.Content);
        }

        using (var disk = new Disk(ms, Ownership.Dispose))
        {
            Assert.Equal(geometry, disk.Geometry);
            Assert.NotNull(disk.Content);
        }
    }

    [Fact]
    public void ConstructorFromFilesDynamic()
    {
        var baseStream = new MemoryStream();
        var baseFile = DiskImageFile.InitializeDynamic(baseStream, Ownership.Dispose, 16 * Sizes.OneGiB);

        var childStream = new MemoryStream();
        var childFile = DiskImageFile.InitializeDifferencing(childStream, Ownership.Dispose, baseFile, @"C:\temp\foo.vhd", @".\foo.vhd", DateTime.Now);

        var grandChildStream = new MemoryStream();
        var grandChildFile = DiskImageFile.InitializeDifferencing(grandChildStream, Ownership.Dispose, childFile, @"C:\temp\child1.vhd", @".\child1.vhd", DateTime.Now);

        using var disk = new Disk([grandChildFile, childFile, baseFile], Ownership.Dispose);
        Assert.NotNull(disk.Content);
    }

    [Fact]
    public void ConstructorFromFilesFixed()
    {
        var baseStream = new MemoryStream();
        var baseFile = DiskImageFile.InitializeFixed(baseStream, Ownership.Dispose, 16 * Sizes.OneMiB);

        var childStream = new MemoryStream();
        var childFile = DiskImageFile.InitializeDifferencing(childStream, Ownership.Dispose, baseFile, @"C:\temp\foo.vhd", @".\foo.vhd", DateTime.Now);

        var grandChildStream = new MemoryStream();
        var grandChildFile = DiskImageFile.InitializeDifferencing(grandChildStream, Ownership.Dispose, childFile, @"C:\temp\child1.vhd", @".\child1.vhd", DateTime.Now);

        using var disk = new Disk([grandChildFile, childFile, baseFile], Ownership.Dispose);
        Assert.NotNull(disk.Content);
    }


}
