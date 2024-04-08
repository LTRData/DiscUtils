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
using DiscUtils.Streams;
using DiscUtils.Vdi;
using Xunit;

namespace LibraryTests.Vdi
{
    public class StreamTest
    {
        [Fact]
        public void Attributes()
        {
            var stream = new MemoryStream();
            using var disk = Disk.InitializeDynamic(stream, Ownership.Dispose, 16 * 1024L * 1024 * 1024);
            Stream s = disk.Content;
            Assert.True(s.CanRead);
            Assert.True(s.CanWrite);
            Assert.True(s.CanSeek);
        }

        [Fact]
        public void ReadWriteSmall()
        {
            var stream = new MemoryStream();
            using (var disk = Disk.InitializeDynamic(stream, Ownership.None, 16 * 1024L * 1024 * 1024))
            {
                var content = new byte[100];
                for(var i = 0; i < content.Length; ++i)
                {
                    content[i] = (byte)i;
                }

                Stream s = disk.Content;
                s.Write(content, 10, 40);
                Assert.Equal(40, s.Position);
                s.Write(content, 50, 50);
                Assert.Equal(90, s.Position);
                s.Position = 0;

                var buffer = new byte[100];
                s.Read(buffer, 10, 60);
                Assert.Equal(60, s.Position);
                for (var i = 0; i < 10; ++i)
                {
                    Assert.Equal(0, buffer[i]);
                }

                for (var i = 10; i < 60; ++i)
                {
                    Assert.Equal(i, buffer[i]);
                }
            }

            // Check the data persisted
            using (var disk = new Disk(stream))
            {
                Stream s = disk.Content;

                var buffer = new byte[100];
                s.Read(buffer, 10, 20);
                Assert.Equal(20, s.Position);
                for (var i = 0; i < 10; ++i)
                {
                    Assert.Equal(0, buffer[i]);
                }

                for (var i = 10; i < 20; ++i)
                {
                    Assert.Equal(i, buffer[i]);
                }
            }
        }

        [Fact]
        public void ReadWriteLarge()
        {
            var stream = new MemoryStream();
            using var disk = Disk.InitializeDynamic(stream, Ownership.Dispose, 16 * 1024L * 1024 * 1024);
            var content = new byte[3 * 1024 * 1024];
            for (var i = 0; i < content.Length; ++i)
            {
                content[i] = (byte)i;
            }

            Stream s = disk.Content;
            s.Position = 10;
            s.Write(content, 0, content.Length);

            var buffer = new byte[content.Length];
            s.Position = 10;
            s.Read(buffer, 0, buffer.Length);

            for (var i = 0; i < content.Length; ++i)
            {
                if (buffer[i] != content[i])
                {
                    Assert.Fail("Corrupt stream contents");
                }
            }
        }

        [Fact]
        public void DisposeTest()
        {
            Stream contentStream;

            var stream = new MemoryStream();
            using (var disk = Disk.InitializeDynamic(stream, Ownership.None, 16 * 1024L * 1024 * 1024))
            {
                contentStream = disk.Content;
            }

            try
            {
                contentStream.Position = 0;
                Assert.Fail("Able to use stream after disposed");
            }
            catch(ObjectDisposedException) { }
        }

        [Fact]
        public void ReadNotPresent()
        {
            var stream = new MemoryStream();
            using var disk = Disk.InitializeDynamic(stream, Ownership.Dispose, 16 * 1024L * 1024 * 1024);
            var buffer = new byte[100];
            disk.Content.Seek(2 * 1024 * 1024, SeekOrigin.Current);
            disk.Content.ReadExactly(buffer, 0, buffer.Length);

            for (var i = 0; i < 100; ++i)
            {
                if (buffer[i] != 0)
                {
                    Assert.True(false);
                }
            }
        }
    }
}
