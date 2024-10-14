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
using System.Reflection;
using DiscUtils.SquashFs;
using K4os.Compression.LZ4.Encoders;
using lzo.net;
using Xunit;

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

#if TEST_EMBEDDED_LZO
    [Fact]
    public void DecompressLzo()
    {
        var fsImage = Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(SquashFileSystemReaderTest), @"TestData.test_lzo.sqfs")
            ?? throw new InvalidOperationException("Missing embedded test file");

        var testData = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(SquashFileSystemReaderTest), @"TestData.test.txt"))?.ReadToEnd()
            ?? throw new InvalidOperationException("Missing embedded test file");

        var reader = new SquashFileSystemReader(fsImage, new SquashFileSystemReaderOptions()
        {
            GetDecompressor = (kind, options) => kind == SquashFileSystemCompressionKind.Lzo ? static stream => new LzoStream(stream, System.IO.Compression.CompressionMode.Decompress) : null
        });

        using var fs = reader.OpenFile("test.txt", FileMode.Open);
        var readData = new StreamReader(fs).ReadToEnd();

        Assert.Equal(testData, readData);
    }
#endif

#if TEST_EMBEDDED_LZ4
    [Fact]
    public void DecompressLz4()
    {
        var fsImage = Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(SquashFileSystemReaderTest), @"TestData.test_lz4.sqfs")
            ?? throw new InvalidOperationException("Missing embedded test file");

        var testData = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(SquashFileSystemReaderTest), @"TestData.test.txt"))?.ReadToEnd()
            ?? throw new InvalidOperationException("Missing embedded test file");

        var reader = new SquashFileSystemReader(fsImage, new SquashFileSystemReaderOptions()
        {
            GetDecompressor = (kind, options) => kind == SquashFileSystemCompressionKind.Lz4 ? static stream => new SimpleLz4Stream(stream) : null
        });

        using var fs = reader.OpenFile("test.txt", FileMode.Open);
        var readData = new StreamReader(fs).ReadToEnd();

        Assert.Equal(testData, readData);
    }
#endif
}
