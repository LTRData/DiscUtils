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
using DiscUtils;
using DiscUtils.Ntfs;
using DiscUtils.Streams;

namespace LibraryTests
{
    public delegate DiscFileSystem NewFileSystemDelegate();

    public static class FileSystemSource
    {
        public static IEnumerable<object[]> ReadWriteFileSystems
        {
            get
            {
                yield return new object[] { new NewFileSystemDelegate(FatFileSystem) };

                // TODO: When format code complete, format a vanilla partition rather than relying on file on disk
                yield return new object[] { new NewFileSystemDelegate(DiagnosticNtfsFileSystem) };
            }
        }

        public static IEnumerable<object[]> QuickReadWriteFileSystems
        {
            get
            {
                yield return new object[] { new NewFileSystemDelegate(FatFileSystem) };
                yield return new object[] { new NewFileSystemDelegate(NtfsFileSystem) };
            }
        }

        private static DiscUtils.Fat.FatFileSystem FatFileSystem()
        {
            var buffer = new SparseMemoryBuffer(4096);
            var ms = new SparseMemoryStream();
            var diskGeometry = Geometry.FromCapacity(30 * 1024 * 1024);
            return DiscUtils.Fat.FatFileSystem.FormatFloppy(ms, FloppyDiskType.Extended, null);
        }

        public static DiscFileSystem DiagnosticNtfsFileSystem()
        {
            var buffer = new SparseMemoryBuffer(4096);
            var ms = new SparseMemoryStream();
            var diskGeometry = Geometry.FromCapacity(30 * 1024 * 1024);
            DiscUtils.Ntfs.NtfsFileSystem.Format(ms, "", diskGeometry, 0, diskGeometry.TotalSectorsLong);
            var discFs = new DiscUtils.Diagnostics.ValidatingFileSystem<NtfsFileSystem, NtfsFileSystemChecker>(ms)
            {
                CheckpointInterval = 1,
                GlobalIOTraceCapturesStackTraces = false
            };
            return discFs;
        }

        public static DiscUtils.Ntfs.NtfsFileSystem NtfsFileSystem()
        {
            var buffer = new SparseMemoryBuffer(4096);
            var ms = new SparseMemoryStream();
            var diskGeometry = Geometry.FromCapacity(30 * 1024 * 1024);
            return DiscUtils.Ntfs.NtfsFileSystem.Format(ms, "", diskGeometry, 0, diskGeometry.TotalSectorsLong);
        }

    }
}
