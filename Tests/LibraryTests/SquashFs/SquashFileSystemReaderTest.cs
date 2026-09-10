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
    /// An image made by mksquashfs with a gzip compression level (so the superblock carries compressor options), a
    /// 128 KB block size, and files mksquashfs stores in extended file inodes: a sparse one (2 MB of zeros stored as
    /// nothing, then "end" in a fragment) and a hard-linked one. text.bin spans two full blocks and a fragment.
    /// <see cref="SquashFixtures"/> holds the recipe and builds the image fresh when mksquashfs is on the machine.
    /// </summary>
    [Fact]
    public void ExtendedFileInodesSparseBlocksAndCompressorOptions()
    {
        using var fs = OpenImage("extended-inodes.sqsh");

        Assert.Equal("hello", ReadText(fs, "small.txt"));
        Assert.Equal("twice", ReadText(fs, "hard.txt"));
        Assert.Equal("twice", ReadText(fs, @"dir\hard2.txt"));

        Assert.Equal(SquashFixtures.Text, ReadText(fs, "text.bin"));

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

    /// <summary>
    /// One Read() call that starts in one stored block and ends in the next must return the bytes of both: the
    /// offset within the second block is where the read has got to, not where it started. text.bin has 128 KB
    /// blocks, so a read across offset 131072 crosses two stored blocks, and one across 262144 crosses from the
    /// last stored block into the fragment that holds the tail.
    /// </summary>
    [Theory]
    [InlineData(131072 - 500, 1000)]
    [InlineData(131072 - 1, 2)]
    [InlineData(262144 - 700, 1400)]
    [InlineData(0, 300000)]
    public void SingleReadAcrossStoredBlockBoundary(int offset, int count)
    {
        using var fs = OpenImage("extended-inodes.sqsh");
        var expected = Encoding.ASCII.GetBytes(SquashFixtures.Text).AsSpan(offset, count).ToArray();
        using var stream = fs.OpenFile("text.bin", FileMode.Open, FileAccess.Read);
        var buffer = new byte[count];
        stream.Position = offset;
        var read = stream.Read(buffer, 0, count);
        Assert.Equal(count, read);
        Assert.Equal(expected, buffer);
    }

    /// <summary>
    /// A file larger than 4 GiB only fits an extended inode (the basic one has a 32-bit size). huge.bin is
    /// 4 GiB + 4 bytes: zeros, "mid" at 2 GiB + 5, and "end!" after the 4 GiB mark, in a 1 MiB-block image; nearly
    /// every block is sparse, so the image is 4 KB and the reads cost nothing.
    /// </summary>
    [Fact]
    public void SparseFileLargerThan4GiB()
    {
        const long size = 4L * 1024 * 1024 * 1024 + 4;
        const long mid = 2L * 1024 * 1024 * 1024 + 5;
        using var fs = OpenImage("huge-sparse.sqsh");
        Assert.Equal(size, fs.GetFileLength("huge.bin"));

        using var stream = fs.OpenFile("huge.bin", FileMode.Open, FileAccess.Read);
        Assert.Equal(size, stream.Length);

        var head = new byte[4096];
        Assert.Equal(head.Length, stream.Read(head, 0, head.Length));
        Assert.All(head, b => Assert.Equal(0, b));

        // one read from a sparse block into the block that holds "mid"
        var around = new byte[32];
        stream.Position = mid - 15;
        Assert.Equal(around.Length, stream.Read(around, 0, around.Length));
        var expected = new byte[32];
        Encoding.ASCII.GetBytes("mid").CopyTo(expected, 15);
        Assert.Equal(expected, around);

        // the tail beyond the 4 GiB mark: the last stored block's end and the fragment
        var tail = new byte[10];
        stream.Position = size - 10;
        Assert.Equal(tail.Length, stream.Read(tail, 0, tail.Length));
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, (byte)'e', (byte)'n', (byte)'d', (byte)'!' }, tail);
        Assert.Equal(0, stream.Read(tail, 0, tail.Length));
    }

    /// <summary>
    /// mksquashfs stores a directory whose table exceeds one metadata block as an extended directory inode (with an
    /// index), the root included; the reader used to cast the root to the basic inode type. The image is built by
    /// mksquashfs at test time and has no embedded fallback, so the test is a no-op where the tool is absent.
    /// </summary>
    [Fact]
    public void RootStoredAsExtendedDirectoryInode()
    {
        using var image = SquashFixtures.OpenIfBuilt("big-root.sqsh");
        if (image is null)
        {
            return;
        }

        using var fs = new SquashFileSystemReader(image);
        Assert.Equal(1001, fs.GetFileSystemEntries("").Count());
        Assert.True(fs.FileExists("entry-0999"));
        Assert.Equal(0, fs.GetFileLength("entry-0999"));
    }

    /// <summary>
    /// A symbolic link's target is the path stored after its inode; a lookup through the link reaches the target
    /// (the reader used to throw NotImplementedException from any path that landed on a symlink).
    /// </summary>
    [Fact]
    public void SymlinkTargetIsRead()
    {
        using var image = SquashFixtures.OpenIfBuilt("big-root.sqsh");
        if (image is null)
        {
            return;
        }

        using var fs = new SquashFileSystemReader(image);
        Assert.True(fs.FileExists("link"));
        Assert.Equal("first", ReadText(fs, "link"));
        Assert.Equal(5, fs.GetFileLength("link"));
    }

    private static SquashFileSystemReader OpenImage(string name) => new(SquashFixtures.Open(name));

    private static string ReadText(SquashFileSystemReader fs, string path)
    {
        using var stream = fs.OpenFile(path, FileMode.Open, FileAccess.Read);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return reader.ReadToEnd();
    }
}
