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
using DiscUtils.Vfs;
using DiscUtils.Streams;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Iso9660;

internal class File : IVfsFile
{
    protected IsoContext _context;
    protected ReaderDirEntry _dirEntry;

    public File(IsoContext context, ReaderDirEntry dirEntry)
    {
        _context = context;
        _dirEntry = dirEntry;
    }

    public virtual byte[] SystemUseData => _dirEntry.RecordExtents[0].SystemUseData;

    public UnixFileSystemInfo UnixFileInfo
    {
        get
        {
            if (!_context.SuspDetected || string.IsNullOrEmpty(_context.RockRidgeIdentifier))
            {
                throw new InvalidOperationException("No RockRidge file information available");
            }

            var suspRecords = new SuspRecords(_context, SystemUseData);

            var pfi = suspRecords.GetEntry<PosixFileInfoSystemUseEntry>(_context.RockRidgeIdentifier, "PX");
            if (pfi != null)
            {
                return new UnixFileSystemInfo
                {
                    FileType = (UnixFileType)((pfi.FileMode >> 12) & 0xff),
                    Permissions = (UnixFilePermissions)(pfi.FileMode & 0xfff),
                    UserId = (int)pfi.UserId,
                    GroupId = (int)pfi.GroupId,
                    Inode = pfi.Inode,
                    LinkCount = (int)pfi.NumLinks
                };
            }

            throw new InvalidOperationException("No RockRidge file information available for this file");
        }
    }

    public DateTime LastAccessTimeUtc
    {
        get => _dirEntry.LastAccessTimeUtc;

        set => throw new NotSupportedException();
    }

    public DateTime LastWriteTimeUtc
    {
        get => _dirEntry.LastWriteTimeUtc;

        set => throw new NotSupportedException();
    }

    public DateTime CreationTimeUtc
    {
        get => _dirEntry.CreationTimeUtc;

        set => throw new NotSupportedException();
    }

    public FileAttributes FileAttributes
    {
        get => _dirEntry.FileAttributes;

        set => throw new NotSupportedException();
    }

    public long FileLength => _dirEntry.RecordExtentsDataLength;

    public IBuffer FileContent
    {
        get
        {
            if (_dirEntry.RecordExtents is [var extent])
            {
                return new StreamBuffer(
                    new ExtentStream(_context.DataStream, extent.LocationOfExtent, extent.DataLength, extent.FileUnitSize, extent.InterleaveGapSize),
                    Ownership.Dispose
                );
            }
            
            return new StreamBuffer(
                new ConcatStream(
                    Ownership.Dispose,
                    _dirEntry.RecordExtents.Select(
                        e => SparseStream.FromStream(
                            new ExtentStream(_context.DataStream, e.LocationOfExtent, e.DataLength, e.FileUnitSize, e.InterleaveGapSize),
                            Ownership.Dispose
                        )
                    )
                ),
                Ownership.Dispose
            );
        }
    }

    public IEnumerable<StreamExtent> EnumerateAllocationExtents()
        => _dirEntry.RecordExtents.Select(e => new StreamExtent(e.LocationOfExtent * IsoUtilities.SectorSize, e.DataLength));
}
