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
using DiscUtils.Core.WindowsSecurity.AccessControl;
using DiscUtils;
using DiscUtils.Ntfs;
using DiscUtils.Streams;
using Xunit;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Threading;
using DiscUtils.Streams.Compatibility;

namespace LibraryTests.Ntfs
{
    public class NtfsFileSystemAsyncTest
    {
        [Fact]
        public async Task ClusterInfo()
        {
            // 'Big' files have clusters
            var ntfs = FileSystemSource.NtfsFileSystem();
            using (var s = ntfs.OpenFile(@"file", FileMode.Create, FileAccess.ReadWrite))
            {
                await s.WriteAsync(new byte[(int)ntfs.ClusterSize]);
            }

            var ranges = ntfs.PathToClusters("file").ToArray();
            Assert.Single(ranges);
            Assert.Equal(1, ranges[0].Count);

            // Short files have no clusters (stored in MFT)
            using (var s = ntfs.OpenFile(@"file2", FileMode.Create, FileAccess.ReadWrite))
            {
                await s.WriteAsync(new byte[] { 0x01 });
            }
            ranges = ntfs.PathToClusters("file2").ToArray();
            Assert.Empty(ranges);
        }

        [Fact]
        public async Task ExtentInfo()
        {
            using var ms = new SparseMemoryStream();
            var diskGeometry = Geometry.FromCapacity(30 * 1024 * 1024);
            var ntfs = NtfsFileSystem.Format(ms, "", diskGeometry, 0, diskGeometry.TotalSectorsLong);

            // Check non-resident attribute
            using (var s = ntfs.OpenFile(@"file", FileMode.Create, FileAccess.ReadWrite))
            {
                var data = new byte[(int)ntfs.ClusterSize];
                data[0] = 0xAE;
                data[1] = 0x3F;
                data[2] = 0x8D;
                await s.WriteAsync(data);
            }

            var extents = ntfs.PathToExtents("file").ToArray();
            Assert.Single(extents);
            Assert.Equal(ntfs.ClusterSize, extents[0].Length);

            ms.Position = extents[0].Start;
            Assert.Equal(0xAE, ms.ReadByte());
            Assert.Equal(0x3F, ms.ReadByte());
            Assert.Equal(0x8D, ms.ReadByte());

            // Check resident attribute
            using (var s = ntfs.OpenFile(@"file2", FileMode.Create, FileAccess.ReadWrite))
            {
                s.WriteByte(0xBA);
                s.WriteByte(0x82);
                s.WriteByte(0x2C);
            }
            extents = ntfs.PathToExtents("file2").ToArray();
            Assert.Single(extents);
            Assert.Equal(3, extents[0].Length);

            var read = new byte[100];
            ms.Position = extents[0].Start;
            await ms.ReadAsync(read);

            Assert.Equal(0xBA, read[0]);
            Assert.Equal(0x82, read[1]);
            Assert.Equal(0x2C, read[2]);
        }

        [Fact]
        public async Task GetFileLength()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            ntfs.OpenFile(@"AFILE.TXT", FileMode.Create).Dispose();
            Assert.Equal(0, ntfs.GetFileLength("AFILE.TXT"));

            using (var stream = ntfs.OpenFile(@"AFILE.TXT", FileMode.Open))
            {
                await stream.WriteAsync(new byte[14325]);
            }
            Assert.Equal(14325, ntfs.GetFileLength("AFILE.TXT"));

            using (var attrStream = ntfs.OpenFile(@"AFILE.TXT:altstream", FileMode.Create))
            {
                await attrStream.WriteAsync(new byte[122]);
            }
            Assert.Equal(122, ntfs.GetFileLength("AFILE.TXT:altstream"));

            // Test NTFS options for hardlink behaviour
            ntfs.CreateDirectory("Dir");
            ntfs.CreateHardLink("AFILE.TXT", @"Dir\OtherLink.txt");

            using (var stream = ntfs.OpenFile("AFILE.TXT", FileMode.Open, FileAccess.ReadWrite))
            {
                stream.SetLength(50);
            }
            Assert.Equal(50, ntfs.GetFileLength("AFILE.TXT"));
            Assert.Equal(14325, ntfs.GetFileLength(@"Dir\OtherLink.txt"));

            ntfs.NtfsOptions.FileLengthFromDirectoryEntries = false;

            Assert.Equal(50, ntfs.GetFileLength(@"Dir\OtherLink.txt"));
        }

        [Fact]
        public async Task Fragmented()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            ntfs.CreateDirectory(@"DIR");

            var buffer = new byte[4096];

            for(var i = 0; i < 2500; ++i)
            {
                using(var stream = ntfs.OpenFile(@$"DIR\file{i}.bin", FileMode.Create, FileAccess.ReadWrite))
                {
                    await stream.WriteAsync(buffer);
                }

                using(var stream = ntfs.OpenFile(@$"DIR\{i}.bin", FileMode.Create, FileAccess.ReadWrite))
                {
                    await stream.WriteAsync(buffer);
                }
            }

            for (var i = 0; i < 2500; ++i)
            {
                ntfs.DeleteFile(@$"DIR\file{i}.bin");
            }

            // Create fragmented file (lots of small writes)
            using (var stream = ntfs.OpenFile(@"DIR\fragmented.bin", FileMode.Create, FileAccess.ReadWrite))
            {
                for (var i = 0; i < 2500; ++i)
                {
                    await stream.WriteAsync(buffer);
                }
            }

            // Try a large write
            var largeWriteBuffer = new byte[200 * 1024];
            for (var i = 0; i < largeWriteBuffer.Length / 4096; ++i)
            {
                largeWriteBuffer[i * 4096] = (byte)i;
            }
            using (var stream = ntfs.OpenFile(@"DIR\fragmented.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                stream.Position = stream.Length - largeWriteBuffer.Length;
                await stream.WriteAsync(largeWriteBuffer);
            }

            // And a large read
            var largeReadBuffer = new byte[largeWriteBuffer.Length];
            using (var stream = ntfs.OpenFile(@"DIR\fragmented.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                stream.Position = stream.Length - largeReadBuffer.Length;
                await stream.ReadExactlyAsync(largeReadBuffer, CancellationToken.None);
            }

            Assert.Equal(largeWriteBuffer, largeReadBuffer);
        }

        [Fact]
        public async Task Sparse()
        {
            var fileSize = 1 * 1024 * 1024;

            var ntfs = FileSystemSource.NtfsFileSystem();

            var data = new byte[fileSize];
            for (var i = 0; i < fileSize; i++)
            {
                data[i] = (byte)i;
            }

            using (var s = ntfs.OpenFile("file.bin", FileMode.CreateNew))
            {
                await s.WriteAsync(data);

                ntfs.SetAttributes("file.bin", ntfs.GetAttributes("file.bin") | FileAttributes.SparseFile);

                s.Position = 64 * 1024;
                s.Clear(128 * 1024);
                s.Position = fileSize - 64 * 1024;
                s.Clear(128 * 1024);
            }

            using (var s = ntfs.OpenFile("file.bin", FileMode.Open))
            {
                Assert.Equal(fileSize + 64 * 1024, s.Length);

                var extents = new List<StreamExtent>(s.Extents);

                Assert.Equal(2, extents.Count);
                Assert.Equal(0, extents[0].Start);
                Assert.Equal(64 * 1024, extents[0].Length);
                Assert.Equal((64 + 128) * 1024, extents[1].Start);
                Assert.Equal(fileSize - (64 * 1024) - ((64 + 128) * 1024), extents[1].Length);

                s.Position = 72 * 1024;
                s.WriteByte(99);

                var readBuffer = new byte[fileSize];
                s.Position = 0;
                await s.ReadExactlyAsync(readBuffer, CancellationToken.None);

                for (var i = 64 * 1024; i < (128 + 64) * 1024; ++i)
                {
                    data[i] = 0;
                }
                for (var i = fileSize - (64 * 1024); i < fileSize; ++i)
                {
                    data[i] = 0;
                }
                data[72 * 1024] = 99;

                Assert.Equal(data, readBuffer);
            }
        }
    }
}
