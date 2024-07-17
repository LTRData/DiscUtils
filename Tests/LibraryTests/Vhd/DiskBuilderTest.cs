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

using System.IO;
using System.Linq;
using DiscUtils.Streams;
using DiscUtils.Vhd;
using Xunit;

namespace LibraryTests.Vhd;

public class DiskBuilderTest
{
    private SparseStream dynamic_diskContent;
    private SparseStream fixed_diskContent;

    public DiskBuilderTest()
    {
        {
            var fileStream = new MemoryStream();
            var baseFile = Disk.InitializeDynamic(fileStream, Ownership.Dispose, 16 * 1024L * 1024);
            for (var i = 0; i < 8; i += 1024 * 1024)
            {
                baseFile.Content.Position = i;
                baseFile.Content.WriteByte((byte)i);
            }

            baseFile.Content.Position = 15 * 1024 * 1024;
            baseFile.Content.WriteByte(0xFF);

            dynamic_diskContent = baseFile.Content;
        }
        {
            var fileStream = new MemoryStream();
            var baseFile = Disk.InitializeFixed(fileStream, Ownership.Dispose, 16 * 1024L * 1024);
            for (var i = 0; i < 8; i += 1024 * 1024)
            {
                baseFile.Content.Position = i;
                baseFile.Content.WriteByte((byte)i);
            }

            baseFile.Content.Position = 15 * 1024 * 1024;
            baseFile.Content.WriteByte(0xFF);

            fixed_diskContent = baseFile.Content;
        }
    }

    [Fact]
    public void BuildFixed()
    {
        var builder = new DiskBuilder
        {
            DiskType = FileType.Fixed,
            Content = fixed_diskContent
        };

        var fileSpecs = builder.Build("foo").ToArray();
        Assert.Single(fileSpecs);
        Assert.Equal("foo.vhd", fileSpecs[0].Name);

        using var disk = new Disk(fileSpecs[0].OpenStream(), Ownership.Dispose);
        for (var i = 0; i < 8; i += 1024 * 1024)
        {
            disk.Content.Position = i;
            Assert.Equal(i, disk.Content.ReadByte());
        }

        disk.Content.Position = 15 * 1024 * 1024;
        Assert.Equal(0xFF, disk.Content.ReadByte());
    }

    [Fact]
    public void BuildDynamic()
    {
        var builder = new DiskBuilder
        {
            DiskType = FileType.Dynamic,
            Content = dynamic_diskContent
        };

        var fileSpecs = builder.Build("foo").ToArray();
        Assert.Single(fileSpecs);
        Assert.Equal("foo.vhd", fileSpecs[0].Name);

        using var disk = new Disk(fileSpecs[0].OpenStream(), Ownership.Dispose);
        for (var i = 0; i < 8; i += 1024 * 1024)
        {
            disk.Content.Position = i;
            Assert.Equal(i, disk.Content.ReadByte());
        }

        disk.Content.Position = 15 * 1024 * 1024;
        Assert.Equal(0xFF, disk.Content.ReadByte());
    }
}
