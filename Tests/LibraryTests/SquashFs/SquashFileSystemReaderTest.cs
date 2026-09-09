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
using System.Text;
using DiscUtils.SquashFs;

namespace LibraryTests.SquashFs;

public sealed class SquashFileSystemReaderTest
{
    [Fact]
    public void Detect()
    {
        var ms = new MemoryStream(new byte[1000]);
        Assert.False(SquashFileSystemReader.Detect(ms));

        ms = new MemoryStream(new byte[10]);
        Assert.False(SquashFileSystemReader.Detect(ms));

        var emptyFs = new MemoryStream();
        var builder = new SquashFileSystemBuilder();
        builder.Build(emptyFs);
        Assert.True(SquashFileSystemReader.Detect(emptyFs));
    }

    /// <summary>
    /// An image made by mksquashfs 4.7 with a gzip compression level (so the superblock carries compressor
    /// options), a 128 KB block size, and files mksquashfs stores in extended file inodes: a sparse one (2 MB of
    /// zeros stored as nothing, then "end" in a fragment) and a hard-linked one. text.bin spans three blocks.
    /// </summary>
    [Fact]
    public void ExtendedFileInodesSparseBlocksAndCompressorOptions()
    {
        using var resource = GetType().Assembly.GetManifestResourceStream(GetType(), "extended-inodes.sqsh")
            ?? throw new InvalidOperationException("Missing test resource extended-inodes.sqsh");
        var image = new MemoryStream();
        resource.CopyTo(image);
        image.Position = 0;

        using var fs = new SquashFileSystemReader(image);

        Assert.Equal("hello", ReadText(fs, "small.txt"));
        Assert.Equal("twice", ReadText(fs, "hard.txt"));
        Assert.Equal("twice", ReadText(fs, @"dir\hard2.txt"));

        var line = "a line of text that compresses well, over and over\n";
        var text = new StringBuilder();
        while (text.Length < 300000)
        {
            text.Append(line);
        }
        Assert.Equal(text.ToString(0, 300000), ReadText(fs, "text.bin"));

        Assert.Equal(2 * 1024 * 1024 + 3, fs.GetFileLength("sparse.bin"));
        using var sparse = fs.OpenFile("sparse.bin", FileMode.Open, FileAccess.Read);
        var buffer = new byte[300000];
        sparse.Position = 1024 * 1024 - 100000;
        Assert.Equal(buffer.Length, sparse.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, b => Assert.Equal(0, b));
        sparse.Position = 2 * 1024 * 1024 - 2;
        var tail = new byte[5];
        Assert.Equal(5, sparse.Read(tail, 0, tail.Length));
        Assert.Equal(new byte[] { 0, 0, (byte)'e', (byte)'n', (byte)'d' }, tail);
    }

    private static string ReadText(SquashFileSystemReader fs, string path)
    {
        using var stream = fs.OpenFile(path, FileMode.Open, FileAccess.Read);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return reader.ReadToEnd();
    }
}
