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
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Threading;
using DiscUtils.Streams.Compatibility;
using System;

namespace LibraryTests.Ntfs
{
    public class NtfsFileSystemTest
    {
        [Fact]
        public void RecursiveDelete()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            //ntfs.NtfsOptions.HideHiddenFiles = false;
            //ntfs.NtfsOptions.HideSystemFiles = false;

            ntfs.CreateDirectory("Dir");

            ntfs.CreateDirectory(@"Dir\SubDir");

            var filePath = Path.Combine("Dir", "SubDir", "File.bin");

            ntfs.OpenFile(filePath, FileMode.Create, FileAccess.ReadWrite).Close();

            ntfs.SetAttributes(filePath, FileAttributes.Hidden | FileAttributes.System);

            var info = ntfs.GetDirectoryInfo("Dir");

            Assert.True(info.Exists);

            info.Delete(recursive: true);

            info = ntfs.GetDirectoryInfo("Dir");
            
            Assert.False(info.Exists);
        }

        [Fact]//(Skip = "Issue #14")]
        public void AclInheritance()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            var sd = new RawSecurityDescriptor("O:BAG:BAD:(A;OICINP;GA;;;BA)");
            ntfs.CreateDirectory("dir");
            ntfs.SetSecurity("dir", sd);

            ntfs.CreateDirectory(@"dir\subdir");
            var inheritedSd = ntfs.GetSecurity(@"dir\subdir");

            Assert.NotNull(inheritedSd);
            Assert.Equal("O:BAG:BAD:(A;ID;GA;;;BA)", inheritedSd.GetSddlForm(AccessControlSections.All));

            using (ntfs.OpenFile(@"dir\subdir\file", FileMode.Create, FileAccess.ReadWrite)) { }
            inheritedSd = ntfs.GetSecurity(@"dir\subdir\file");
            Assert.NotNull(inheritedSd);
            Assert.Equal("O:BAG:BAD:", inheritedSd.GetSddlForm(AccessControlSections.All));
        }

        [Fact]
        public void ReparsePoints_Empty()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            ntfs.CreateDirectory("dir");
            ntfs.SetReparsePoint("dir", new ReparsePoint(12345, System.Array.Empty<byte>()));

            var rp = ntfs.GetReparsePoint("dir");

            Assert.Equal(12345, rp.Tag);
            Assert.NotNull(rp.Content);
            Assert.Empty(rp.Content);
        }

        [Fact]
        public void ReparsePoints_NonEmpty()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            ntfs.CreateDirectory("dir");
            ntfs.SetReparsePoint("dir", new ReparsePoint(123, new byte[] { 4, 5, 6 }));

            var rp = ntfs.GetReparsePoint("dir");

            Assert.Equal(123, rp.Tag);
            Assert.NotNull(rp.Content);
            Assert.Equal(3, rp.Content.Length);
        }

        [Fact]//(Skip = "Issue #14")]
        public void Format_SmallDisk()
        {
            long size = 8 * 1024 * 1024;
            var partStream = new SparseMemoryStream();
            //VirtualDisk disk = Vhd.Disk.InitializeDynamic(partStream, Ownership.Dispose, size);
            NtfsFileSystem.Format(partStream, "New Partition", Geometry.FromCapacity(size), 0, size / 512);

            var ntfs = new NtfsFileSystem(partStream);
            ntfs.Dump(TextWriter.Null, "");
        }

        [Fact]//(Skip = "Issue #14")]
        public void Format_LargeDisk()
        {
            var size = 1024L * 1024 * 1024L * 1024; // 1 TB
            var partStream = new SparseMemoryStream();
            NtfsFileSystem.Format(partStream, "New Partition", Geometry.FromCapacity(size), 0, size / 512);

            var ntfs = new NtfsFileSystem(partStream);
            ntfs.Dump(TextWriter.Null, "");
        }

        [Fact]
        public void ClusterInfo()
        {
            // 'Big' files have clusters
            var ntfs = FileSystemSource.NtfsFileSystem();
            using (var s = ntfs.OpenFile(@"file", FileMode.Create, FileAccess.ReadWrite))
            {
                s.Write(new byte[(int)ntfs.ClusterSize], 0, (int)ntfs.ClusterSize);
            }

            var ranges = ntfs.PathToClusters("file").ToArray();
            Assert.Single(ranges);
            Assert.Equal(1, ranges[0].Count);

            // Short files have no clusters (stored in MFT)
            using (var s = ntfs.OpenFile(@"file2", FileMode.Create, FileAccess.ReadWrite))
            {
                s.WriteByte(1);
            }
            ranges = ntfs.PathToClusters("file2").ToArray();
            Assert.Empty(ranges);
        }

        [Fact]
        public async Task ClusterInfoAsync()
        {
            // 'Big' files have clusters
            var ntfs = FileSystemSource.NtfsFileSystem();
            using (var s = ntfs.OpenFile(@"file", FileMode.Create, FileAccess.ReadWrite))
            {
                await s.WriteAsync(new byte[(int)ntfs.ClusterSize], CancellationToken.None);
            }

            var ranges = ntfs.PathToClusters("file").ToArray();
            Assert.Single(ranges);
            Assert.Equal(1, ranges[0].Count);

            // Short files have no clusters (stored in MFT)
            using (var s = ntfs.OpenFile(@"file2", FileMode.Create, FileAccess.ReadWrite))
            {
                s.WriteByte(1);
            }
            ranges = ntfs.PathToClusters("file2").ToArray();
            Assert.Empty(ranges);
        }

        [Fact]
        public void ClusterInfoSpan()
        {
            // 'Big' files have clusters
            var ntfs = FileSystemSource.NtfsFileSystem();
            using (var s = ntfs.OpenFile(@"file", FileMode.Create, FileAccess.ReadWrite))
            {
                s.Write(new byte[(int)ntfs.ClusterSize]);
            }

            var ranges = ntfs.PathToClusters("file").ToArray();
            Assert.Single(ranges);
            Assert.Equal(1, ranges[0].Count);

            // Short files have no clusters (stored in MFT)
            using (var s = ntfs.OpenFile(@"file2", FileMode.Create, FileAccess.ReadWrite))
            {
                s.WriteByte(1);
            }
            ranges = ntfs.PathToClusters("file2").ToArray();
            Assert.Empty(ranges);
        }

        [Fact]
        public void ExtentInfo()
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
                s.Write(data, 0, (int)ntfs.ClusterSize);
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
            ms.Read(read, 0, 100);

            Assert.Equal(0xBA, read[0]);
            Assert.Equal(0x82, read[1]);
            Assert.Equal(0x2C, read[2]);
        }

        [Fact]
        public void ManyAttributes()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();
            using (var s = ntfs.OpenFile(@"file", FileMode.Create, FileAccess.ReadWrite))
            {
                s.WriteByte(32);
            }

            for (var i = 0; i < 50; ++i)
            {
                ntfs.CreateHardLink("file", $"hl{i}");
            }

            using (var s = ntfs.OpenFile("hl35", FileMode.Open, FileAccess.ReadWrite))
            {
                Assert.Equal(32, s.ReadByte());
                s.Position = 0;
                s.WriteByte(12);
            }

            using (var s = ntfs.OpenFile("hl5", FileMode.Open, FileAccess.ReadWrite))
            {
                Assert.Equal(12, s.ReadByte());
            }

            for (var i = 0; i < 50; ++i)
            {
                ntfs.DeleteFile($"hl{i}");
            }

            Assert.Single(ntfs.GetFiles(@"\"));

            ntfs.DeleteFile("file");

            Assert.Empty(ntfs.GetFiles(@"\"));
        }

        [Fact]
        public void ShortNames()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            // Check we can find a short name in the same directory
            using (var s = ntfs.OpenFile("ALongFileName.txt", FileMode.CreateNew)) {}
            ntfs.SetShortName("ALongFileName.txt", "ALONG~01.TXT");
            Assert.Equal("ALONG~01.TXT", ntfs.GetShortName("ALongFileName.txt"));
            Assert.True(ntfs.FileExists("ALONG~01.TXT"));

            // Check path handling
            ntfs.CreateDirectory("DIR");
            using (var s = ntfs.OpenFile(@"DIR\ALongFileName2.txt", FileMode.CreateNew)) { }
            ntfs.SetShortName(@"DIR\ALongFileName2.txt", "ALONG~02.TXT");
            Assert.Equal("ALONG~02.TXT", ntfs.GetShortName(@"DIR\ALongFileName2.txt"));
            Assert.True(ntfs.FileExists(@"DIR\ALONG~02.TXT"));

            // Check we can open a file by the short name
            using (var s = ntfs.OpenFile("ALONG~01.TXT", FileMode.Open)) { }

            // Delete the long name, and make sure the file is gone
            ntfs.DeleteFile("ALONG~01.TXT");
            Assert.False(ntfs.FileExists("ALONG~01.TXT"));

            // Delete the short name, and make sure the file is gone
            ntfs.DeleteFile(@"DIR\ALONG~02.TXT");
            Assert.False(ntfs.FileExists(@"DIR\ALongFileName2.txt"));
        }

        [Fact]
        public void HardLinkCount()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            using (var s = ntfs.OpenFile("ALongFileName.txt", FileMode.CreateNew)) { }
            Assert.Equal(1, ntfs.GetHardLinkCount("ALongFileName.txt"));

            ntfs.CreateHardLink("ALongFileName.txt", "AHardLink.TXT");
            Assert.Equal(2, ntfs.GetHardLinkCount("ALongFileName.txt"));

            ntfs.CreateDirectory("DIR");
            ntfs.CreateHardLink(@"ALongFileName.txt", @"DIR\SHORTLNK.TXT");
            Assert.Equal(3, ntfs.GetHardLinkCount("ALongFileName.txt"));

            // If we enumerate short names, then the initial long name results in two 'hardlinks'
            ntfs.NtfsOptions.HideDosFileNames = false;
            Assert.Equal(4, ntfs.GetHardLinkCount("ALongFileName.txt"));
        }

        [Fact]
        public void HasHardLink()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            using (var s = ntfs.OpenFile("ALongFileName.txt", FileMode.CreateNew)) { }
            Assert.False(ntfs.HasHardLinks("ALongFileName.txt"));

            ntfs.CreateHardLink("ALongFileName.txt", "AHardLink.TXT");
            Assert.True(ntfs.HasHardLinks("ALongFileName.txt"));

            using (var s = ntfs.OpenFile("ALongFileName2.txt", FileMode.CreateNew)) { }

            // If we enumerate short names, then the initial long name results in two 'hardlinks'
            ntfs.NtfsOptions.HideDosFileNames = false;
            Assert.True(ntfs.HasHardLinks("ALongFileName2.txt"));
        }

        [Fact]
        public void MoveLongName()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            using (var s = ntfs.OpenFile("ALongFileName.txt", FileMode.CreateNew)) { }

            Assert.True(ntfs.FileExists("ALONGF~1.TXT"));

            ntfs.MoveFile("ALongFileName.txt", "ADifferentLongFileName.txt");

            Assert.False(ntfs.FileExists("ALONGF~1.TXT"));
            Assert.True(ntfs.FileExists("ADIFFE~1.TXT"));

            ntfs.CreateDirectory("ALongDirectoryName");
            Assert.True(ntfs.DirectoryExists("ALONGD~1"));

            ntfs.MoveDirectory("ALongDirectoryName", "ADifferentLongDirectoryName");
            Assert.False(ntfs.DirectoryExists("ALONGD~1"));
            Assert.True(ntfs.DirectoryExists("ADIFFE~1"));
        }

        [Fact]
        public void OpenRawStream()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

#pragma warning disable 618
            Assert.Null(ntfs.OpenRawStream(@"$Extend\$ObjId", AttributeType.Data, null, FileAccess.Read));
#pragma warning restore 618
        }

        [Fact]
        public void GetAlternateDataStreams()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            ntfs.OpenFile("AFILE.TXT", FileMode.Create).Dispose();
            Assert.Empty(ntfs.GetAlternateDataStreams("AFILE.TXT"));

            ntfs.OpenFile("AFILE.TXT:ALTSTREAM", FileMode.Create).Dispose();
            Assert.Single(ntfs.GetAlternateDataStreams("AFILE.TXT"));
            Assert.Equal("ALTSTREAM", ntfs.GetAlternateDataStreams("AFILE.TXT").First());
        }

        [Fact]
        public void DeleteAlternateDataStreams()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            ntfs.OpenFile("AFILE.TXT", FileMode.Create).Dispose();
            ntfs.OpenFile("AFILE.TXT:ALTSTREAM", FileMode.Create).Dispose();
            Assert.Single(ntfs.GetAlternateDataStreams("AFILE.TXT"));

            ntfs.DeleteFile("AFILE.TXT:ALTSTREAM");
            Assert.Single(ntfs.GetFileSystemEntries(""));
            Assert.Empty(ntfs.GetAlternateDataStreams("AFILE.TXT"));
        }

        [Fact]
        public void DeleteShortNameDir()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            ntfs.CreateDirectory(@"\TestLongName1\TestLongName2");
            ntfs.SetShortName(@"\TestLongName1\TestLongName2", "TESTLO~1");

            Assert.True(ntfs.DirectoryExists(@"\TestLongName1\TESTLO~1"));
            Assert.True(ntfs.DirectoryExists(@"\TestLongName1\TestLongName2"));

            ntfs.DeleteDirectory(@"\TestLongName1", true);

            Assert.False(ntfs.DirectoryExists(@"\TestLongName1"));
        }

        [Fact]
        public void GetFileLength()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            ntfs.OpenFile(@"AFILE.TXT", FileMode.Create).Dispose();
            Assert.Equal(0, ntfs.GetFileLength("AFILE.TXT"));

            using (var stream = ntfs.OpenFile(@"AFILE.TXT", FileMode.Open))
            {
                stream.Write(new byte[14325], 0, 14325);
            }
            Assert.Equal(14325, ntfs.GetFileLength("AFILE.TXT"));

            using (var attrStream = ntfs.OpenFile(@"AFILE.TXT:altstream", FileMode.Create))
            {
                attrStream.Write(new byte[122], 0, 122);
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
        public void Fragmented()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            ntfs.CreateDirectory(@"DIR");

            var buffer = new byte[4096];

            for(var i = 0; i < 2500; ++i)
            {
                using(var stream = ntfs.OpenFile(@$"DIR\file{i}.bin", FileMode.Create, FileAccess.ReadWrite))
                {
                    stream.Write(buffer, 0,buffer.Length);
                }

                using(var stream = ntfs.OpenFile(@$"DIR\{i}.bin", FileMode.Create, FileAccess.ReadWrite))
                {
                    stream.Write(buffer, 0,buffer.Length);
                }
            }

            for (var i = 0; i < 2500; ++i)
            {
                ntfs.DeleteFile($@"DIR\file{i}.bin");
            }

            // Create fragmented file (lots of small writes)
            using (var stream = ntfs.OpenFile(@"DIR\fragmented.bin", FileMode.Create, FileAccess.ReadWrite))
            {
                for (var i = 0; i < 2500; ++i)
                {
                    stream.Write(buffer, 0, buffer.Length);
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
                stream.Write(largeWriteBuffer, 0, largeWriteBuffer.Length);
            }

            // And a large read
            var largeReadBuffer = new byte[largeWriteBuffer.Length];
            using (var stream = ntfs.OpenFile(@"DIR\fragmented.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                stream.Position = stream.Length - largeReadBuffer.Length;
                stream.ReadExactly(largeReadBuffer, 0, largeReadBuffer.Length);
            }

            Assert.Equal(largeWriteBuffer, largeReadBuffer);
        }

        [Fact]
        public void FragmentedSpan()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            ntfs.CreateDirectory(@"DIR");

            var buffer = new byte[4096];

            for (var i = 0; i < 2500; ++i)
            {
                using (var stream = ntfs.OpenFile(@$"DIR\file{i}.bin", FileMode.Create, FileAccess.ReadWrite))
                {
                    stream.Write(buffer);
                }

                using (var stream = ntfs.OpenFile(@$"DIR\{i}.bin", FileMode.Create, FileAccess.ReadWrite))
                {
                    stream.Write(buffer);
                }
            }

            for (var i = 0; i < 2500; ++i)
            {
                ntfs.DeleteFile($@"DIR\file{i}.bin");
            }

            // Create fragmented file (lots of small writes)
            using (var stream = ntfs.OpenFile(@"DIR\fragmented.bin", FileMode.Create, FileAccess.ReadWrite))
            {
                for (var i = 0; i < 2500; ++i)
                {
                    stream.Write(buffer);
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
                stream.Write(largeWriteBuffer);
            }

            // And a large read
            var largeReadBuffer = new byte[largeWriteBuffer.Length];
            using (var stream = ntfs.OpenFile(@"DIR\fragmented.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                stream.Position = stream.Length - largeReadBuffer.Length;
                stream.ReadExactly(largeReadBuffer);
            }

            Assert.Equal(largeWriteBuffer, largeReadBuffer);
        }

        [Fact]
        public async Task FragmentedAsync()
        {
            var ntfs = FileSystemSource.NtfsFileSystem();

            ntfs.CreateDirectory(@"DIR");

            var buffer = new byte[4096];

            for (var i = 0; i < 2500; ++i)
            {
                using (var stream = ntfs.OpenFile(@$"DIR\file{i}.bin", FileMode.Create, FileAccess.ReadWrite))
                {
                    await stream.WriteAsync(buffer);
                }

                using (var stream = ntfs.OpenFile(@$"DIR\{i}.bin", FileMode.Create, FileAccess.ReadWrite))
                {
                    await stream.WriteAsync(buffer);
                }
            }

            for (var i = 0; i < 2500; ++i)
            {
                ntfs.DeleteFile($@"DIR\file{i}.bin");
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
                await stream.ReadExactlyAsync(largeReadBuffer);
            }

            Assert.Equal(largeWriteBuffer, largeReadBuffer);
        }

        [Fact]
        public void Sparse()
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
                s.Write(data, 0, fileSize);

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
                s.ReadExactly(readBuffer, 0, fileSize);

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

        [Fact]
        public void SparseSpan()
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
                s.Write(data.AsSpan(0, fileSize));

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
                s.ReadExactly(readBuffer.AsSpan(0, fileSize));

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

        [Fact]
        public async Task SparseAsync()
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
                await s.WriteAsync(data.AsMemory(0, fileSize));

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
                await s.ReadExactlyAsync(readBuffer.AsMemory(0, fileSize));

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
