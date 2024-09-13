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
using DiscUtils;
using DiscUtils.SquashFs;
using K4os.Compression.LZ4;

using LibraryTests.Helpers;
using Xunit;

namespace LibraryTests.SquashFs;

public sealed class SquashFileSystemBuilderTest
{
    [Fact]
    public void SingleFile()
    {
        var fsImage = new MemoryStream();

        var builder = new SquashFileSystemBuilder();
        var fileData = "Contents of a test file."u8.ToArray();
        builder.AddFile("file", fileData);
        builder.Build(fsImage);

        var reader = new SquashFileSystemReader(fsImage);
        Assert.Single(reader.GetFileSystemEntries("\\"));
        Assert.True(reader.FileExists("file"));
        Assert.Equal(fileData.LongLength, reader.GetFileLength("file"));
        var fileVerifyData = reader.OpenFile("file", FileMode.Open).ReadAll();
        Assert.Equal(fileData, fileVerifyData);
        Assert.False(reader.DirectoryExists("file"));
        Assert.False(reader.FileExists("otherfile"));
    }

    [Fact]
    public void TwoFiles()
    {
        var fsImage = new MemoryStream();

        var builder = new SquashFileSystemBuilder();
        var fileData1 = "This is file 1.\n"u8.ToArray();
        var fileData2 = "This is file 2.\n"u8.ToArray();
        builder.AddFile("file1.txt", fileData1);
        builder.AddFile("file2.txt", fileData2);
        builder.Build(fsImage);

        var reader = new SquashFileSystemReader(fsImage);
        Assert.Equal(2, reader.GetFileSystemEntries("\\").Count());
        Assert.Equal(fileData1.LongLength, reader.GetFileLength("file1.txt"));
        var fileVerifyData1 = reader.OpenFile("file1.txt", FileMode.Open).ReadAll();
        Assert.Equal(fileData1, fileVerifyData1);
        var fileVerifyData2 = reader.OpenFile("file2.txt", FileMode.Open).ReadAll();
        Assert.Equal(fileData2, fileVerifyData2);
        Assert.False(reader.DirectoryExists("file1.txt"));
        Assert.False(reader.FileExists("otherfile"));
    }

    [Fact]
    public void CreateDirs()
    {
        var sep = Path.DirectorySeparatorChar;

        var fsImage = new MemoryStream();

        var builder = new SquashFileSystemBuilder();
        builder.AddFile($"{sep}adir{sep}anotherdir{sep}file", new MemoryStream([1, 2, 3, 4]));
        builder.Build(fsImage);

        var reader = new SquashFileSystemReader(fsImage);
        Assert.True(reader.DirectoryExists($"adir"));
        Assert.True(reader.DirectoryExists($"adir{sep}anotherdir"));
        Assert.True(reader.FileExists($"adir{sep}anotherdir{sep}file"));
    }

    [Fact]
    public void Defaults()
    {
        var fsImage = new MemoryStream();

        var builder = new SquashFileSystemBuilder();
        builder.AddFile(@"file", new MemoryStream([1, 2, 3, 4]));
        builder.AddDirectory(@"dir");

        builder.DefaultUser = 1000;
        builder.DefaultGroup = 1234;
        builder.DefaultFilePermissions = UnixFilePermissions.OwnerAll;
        builder.DefaultDirectoryPermissions = UnixFilePermissions.GroupAll;

        builder.AddFile("file2", new MemoryStream());
        builder.AddDirectory("dir2");

        builder.Build(fsImage);

        var reader = new SquashFileSystemReader(fsImage);

        Assert.Equal(0, reader.GetUnixFileInfo("file").UserId);
        Assert.Equal(0, reader.GetUnixFileInfo("file").GroupId);
        Assert.Equal(UnixFilePermissions.OwnerRead | UnixFilePermissions.OwnerWrite | UnixFilePermissions.GroupRead | UnixFilePermissions.GroupWrite, reader.GetUnixFileInfo("file").Permissions);

        Assert.Equal(0, reader.GetUnixFileInfo("dir").UserId);
        Assert.Equal(0, reader.GetUnixFileInfo("dir").GroupId);
        Assert.Equal(UnixFilePermissions.OwnerAll | UnixFilePermissions.GroupRead | UnixFilePermissions.GroupExecute | UnixFilePermissions.OthersRead | UnixFilePermissions.OthersExecute, reader.GetUnixFileInfo("dir").Permissions);

        Assert.Equal(1000, reader.GetUnixFileInfo("file2").UserId);
        Assert.Equal(1234, reader.GetUnixFileInfo("file2").GroupId);
        Assert.Equal(UnixFilePermissions.OwnerAll, reader.GetUnixFileInfo("file2").Permissions);

        Assert.Equal(1000, reader.GetUnixFileInfo("dir2").UserId);
        Assert.Equal(1234, reader.GetUnixFileInfo("dir2").GroupId);
        Assert.Equal(UnixFilePermissions.GroupAll, reader.GetUnixFileInfo("dir2").Permissions);
    }

    [Fact]
    public void FragmentData()
    {
        var fsImage = new MemoryStream();

        var builder = new SquashFileSystemBuilder();
        builder.AddFile(@"file", new MemoryStream([1, 2, 3, 4]));
        builder.Build(fsImage);

        var reader = new SquashFileSystemReader(fsImage);

        using Stream fs = reader.OpenFile("file", FileMode.Open);
        var buffer = new byte[100];
        var numRead = fs.Read(buffer, 0, 100);

        Assert.Equal(4, numRead);
        Assert.Equal(1, buffer[0]);
        Assert.Equal(2, buffer[1]);
        Assert.Equal(3, buffer[2]);
        Assert.Equal(4, buffer[3]);
    }

    [Fact]
    public void BlockData()
    {
        var testData = new byte[(512 * 1024) + 21];
        for (var i = 0; i < testData.Length; ++i)
        {
            testData[i] = (byte)(i % 33);
        }

        var fsImage = new MemoryStream();

        var builder = new SquashFileSystemBuilder();
        builder.AddFile(@"file", new MemoryStream(testData));
        builder.Build(fsImage);

        var reader = new SquashFileSystemReader(fsImage);

        using Stream fs = reader.OpenFile("file", FileMode.Open);
        var buffer = new byte[(512 * 1024) + 1024];
        var numRead = fs.Read(buffer, 0, buffer.Length);

        Assert.Equal(testData.Length, numRead);
        for (var i = 0; i < testData.Length; ++i)
        {
            Assert.Equal(testData[i], buffer[i] /*, "Data differs at index " + i*/);
        }
    }

    [Fact]
    public void BlockDataLz4()
    {
        var testData = new byte[(512 * 1024) + 21];
        for (var i = 0; i < testData.Length; ++i)
        {
            testData[i] = (byte)(i % 33);
        }

        var fsImage = new MemoryStream();

        var builder = new SquashFileSystemBuilder(new SquashFileSystemBuilderOptions()
        {

            CompressionKind = SquashFileSystemCompressionKind.Lz4,
            GetCompressor = (kind, options) => kind == SquashFileSystemCompressionKind.Lz4 ? static stream => new SimpleLz4Stream(stream) : null
        });

        builder.AddFile(@"file", new MemoryStream(testData));
        builder.Build(fsImage);

        var reader = new SquashFileSystemReader(fsImage, new SquashFileSystemReaderOptions()
        {
            GetDecompressor = (kind, options) => kind == SquashFileSystemCompressionKind.Lz4 ? static stream => new SimpleLz4Stream(stream) : null
        });

        using Stream fs = reader.OpenFile("file", FileMode.Open);
        var buffer = new byte[(512 * 1024) + 1024];
        var numRead = fs.Read(buffer, 0, buffer.Length);

        Assert.Equal(testData.Length, numRead);
        for (var i = 0; i < testData.Length; ++i)
        {
            Assert.Equal(testData[i], buffer[i] /*, "Data differs at index " + i*/);
        }
    }

    /// <summary>
    /// A simple stream that uses LZ4 to compress and decompress data.
    /// Read and Write methods expect the whole buffer to be read or written as we can only rely on LZ4 standard decode/encode buffer methods.
    /// </summary>
    private sealed class SimpleLz4Stream : Stream
    {
        private MemoryStream _inner;

        public SimpleLz4Stream(MemoryStream inner)
        {
            _inner = inner;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_inner.Position == _inner.Length)
            {
                return 0;
            }
            // This is supposed to be called only once
            var srcBuffer = _inner.GetBuffer();
            var decompressed = LZ4Codec.Decode(srcBuffer, (int)_inner.Position, (int)(_inner.Length - _inner.Position), buffer, offset, count);
            _inner.Position = _inner.Length;
            return decompressed;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.SetLength(count);
            var destBuffer = _inner.GetBuffer();
            var compressed = LZ4Codec.Encode(buffer, offset, count, destBuffer, 0, destBuffer.Length);
            // If the dest buffer is too small, fake that we wrote the whole buffer
            // In that case, the compressed buffer won't be use because it is at least equal to the uncompressed size.
            if (compressed < 0)
            {
                compressed = count;
            }
            _inner.SetLength(compressed);
            _inner.Position = compressed;
        }

#if NET8_0_OR_GREATER
        public override int Read(Span<byte> buffer)
        {
            if (_inner.Position == _inner.Length)
            {
                return 0;
            }
            var srcBuffer = _inner.ToArray();
            var decompressed = LZ4Codec.Decode(new ReadOnlySpan<byte>(srcBuffer, (int)_inner.Position, (int)(_inner.Length - _inner.Position)), buffer);
            _inner.Position = _inner.Length;
            return decompressed;
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _inner.SetLength(buffer.Length);
            var destBuffer = _inner.GetBuffer();
            var compressed = LZ4Codec.Encode(buffer, destBuffer);
            // If the dest buffer is too small, fake that we wrote the whole buffer
            // In that case, the compressed buffer won't be use because it is at least equal to the uncompressed size.
            if (compressed < 0)
            {
                compressed = buffer.Length;
            }
            _inner.SetLength(compressed);
            _inner.Position = compressed;
        }
#endif

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position { get; set; }
    }
}
