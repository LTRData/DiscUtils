//
// Copyright (c) 2008-2013, Kenneth Bell
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

using DiscUtils.Setup;
using DiscUtils.Streams;
using DiscUtils.Vhdx;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace LibraryTests.Vhdx;

public class DiskBuilderTest
{
    private SparseStream diskContent;

    public DiskBuilderTest()
    {
        var sourceStream = new SparseMemoryStream();
        sourceStream.SetLength(160 * 1024L * 1024);
        for (var i = 0; i < 8; ++i)
        {
            sourceStream.Position = i * 1024L * 1024;
            sourceStream.WriteByte((byte)i);
        }

        sourceStream.Position = 150 * 1024 * 1024;
        sourceStream.WriteByte(0xFF);

        diskContent = sourceStream;
    }

    [Fact(Skip = "Fixed size Vhdx not implemeted")]
    public void BuildFixed()
    {
        var builder = new DiskBuilder
        {
            DiskType = DiskType.Fixed,
            Content = diskContent
        };

        var fileSpecs = builder.Build("foo").ToArray();
        Assert.Single(fileSpecs);
        Assert.Equal("foo.vhdx", fileSpecs[0].Name);

        using var disk = new Disk(fileSpecs[0].OpenStream(), Ownership.Dispose);
        for (var i = 0; i < 8; ++i)
        {
            disk.Content.Position = i * 1024L * 1024;
            Assert.Equal(i, disk.Content.ReadByte());
        }

        disk.Content.Position = 150 * 1024 * 1024;
        Assert.Equal(0xFF, disk.Content.ReadByte());
    }

    [Fact]
    public void BuildEmptyDynamic()
    {
        var builder = new DiskBuilder
        {
            DiskType = DiskType.Dynamic,
            Content = new SparseMemoryStream()
        };

        var fileSpecs = builder.Build("foo").ToArray();
        Assert.Single(fileSpecs);
        Assert.Equal("foo.vhdx", fileSpecs[0].Name);

        using var disk = new Disk(fileSpecs[0].OpenStream(), Ownership.Dispose);
        Assert.Equal(0, disk.Content.Length);
    }

    [Fact]
    public void CreateDifferencing()
    {
        SetupHelper.RegisterAssembly(typeof(Disk).Assembly);

        var tempPath = Path.GetTempPath();

        var nameGuid = Guid.NewGuid();

        var parentPath = Path.Combine(tempPath, $"{nameGuid}_parent.vhdx");

        var diffPath = Path.Combine(tempPath, $"{nameGuid}_diff.vhdx");

        try
        {
            using (var parentDiskBuild = DiscUtils.VirtualDisk.CreateDisk("vhdx", "dynamic", parentPath, diskParameters: new() { Capacity = 200L << 20 }, null, null, useAsync: false))
            {
                for (byte i = 0; i < 8; ++i)
                {
                    parentDiskBuild.Content.Position = i * 1024L * 1024;
                    parentDiskBuild.Content.WriteByte(i);
                }

                parentDiskBuild.Content.Position = 150 * 1024 * 1024;
                parentDiskBuild.Content.WriteByte(0xff);
            }

            using var parentDisk = DiscUtils.VirtualDisk.OpenDisk(parentPath, FileAccess.Read, useAsync: false);

            using (var diffDisk = parentDisk.CreateDifferencingDisk(diffPath, useAsync: false))
            {
                for (var i = 0; i < 8; ++i)
                {
                    diffDisk.Content.Position = i * 1024L * 1024;
                    Assert.Equal(i, diffDisk.Content.ReadByte());
                }

                diffDisk.Content.Position = 150 * 1024 * 1024;
                Assert.Equal(0xFF, diffDisk.Content.ReadByte());

                for (byte i = 0; i < 8; ++i)
                {
                    diffDisk.Content.Position = i * 1024L * 1024;
                    diffDisk.Content.WriteByte(0xfe);
                }

                diffDisk.Content.Position = 150 * 1024 * 1024;
                diffDisk.Content.WriteByte(0xfe);
            }

            for (var i = 0; i < 8; ++i)
            {
                parentDisk.Content.Position = i * 1024L * 1024;
                Assert.Equal(i, parentDisk.Content.ReadByte());
            }

            parentDisk.Content.Position = 150 * 1024 * 1024;
            Assert.Equal(0xFF, parentDisk.Content.ReadByte());
        }
        finally
        {
            if (File.Exists(diffPath))
            {
                File.Delete(diffPath);
            }

            if (File.Exists(parentPath))
            {
                File.Delete(parentPath);
            }
        }
    }

    [Fact]
    public void BuildDynamic()
    {
        var builder = new DiskBuilder
        {
            DiskType = DiskType.Dynamic,
            Content = diskContent
        };

        var fileSpecs = builder.Build("foo").ToArray();
        Assert.Single(fileSpecs);
        Assert.Equal("foo.vhdx", fileSpecs[0].Name);

        using var disk = new Disk(fileSpecs[0].OpenStream(), Ownership.Dispose);
        for (var i = 0; i < 8; ++i)
        {
            disk.Content.Position = i * 1024L * 1024;
            Assert.Equal(i, disk.Content.ReadByte());
        }

        disk.Content.Position = 150 * 1024 * 1024;
        Assert.Equal(0xFF, disk.Content.ReadByte());
    }
}
