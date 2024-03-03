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
using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using Xunit;

namespace LibraryTests.Iso9660
{
    public class BuilderTest
    {
        [Fact]
        public void AddFileStream()
        {
            var builder = new CDBuilder();
            builder.AddFile(@"ADIR\AFILE.TXT", new MemoryStream());
            var fs = new CDReader(builder.Build(), false);

            Assert.True(fs.Exists(@"ADIR\AFILE.TXT"));
        }

        [Fact]
        public void AddFileBytes()
        {
            var builder = new CDBuilder();
            builder.AddFile(@"ADIR\AFILE.TXT", System.Array.Empty<byte>());
            var fs = new CDReader(builder.Build(), false);

            Assert.True(fs.Exists(@"ADIR\AFILE.TXT"));
        }

        [Fact]
        public void BootImage()
        {
            var bytes = new byte[3133440];

            for(var i = 0; i < bytes.Length; ++i)
            {
                bytes[i] = (byte)i;
            }

            var stream = new MemoryStream(bytes);

            var partitionTable = BiosPartitionTable.Initialize(stream, Geometry.FromCapacity(stream.Length));

            partitionTable.Create(WellKnownPartitionType.WindowsFat, active: true);

            var builder = new CDBuilder();

            builder.SetBootImage(stream, BootDeviceEmulation.HardDisk, 0x543);

            var fs = new CDReader(builder.Build(), false);

            Assert.True(fs.HasBootImage);

            using (var bootImg = fs.OpenBootImage())
            {
                Assert.Equal(bytes.Length, bootImg.Length);

                for (var i = 0; i < bootImg.Length; ++i)
                {
                    if (bytes[i] != bootImg.ReadByte())
                    {
                        Assert.Fail("Boot image corrupted");
                    }
                }
            }

            Assert.Equal(BootDeviceEmulation.HardDisk, fs.BootEmulation);
            Assert.Equal(0x543, fs.BootLoadSegment);
        }
    }
}
