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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using DiscUtils.Streams;

namespace DiscUtils.LogicalDiskManager;

internal class Database
{
    private readonly Dictionary<ulong, DatabaseRecord> _records;
    private readonly DatabaseHeader _vmdb;

    public Database(Stream stream)
    {
        var dbStart = stream.Position;

        var buffer = ArrayPool<byte>.Shared.Rent(Sizes.Sector);
        try
        {
            stream.ReadExactly(buffer, 0, Sizes.Sector);
            _vmdb = new DatabaseHeader();
            _vmdb.ReadFrom(buffer);

            stream.Position = dbStart + _vmdb.HeaderSize;

            var bufferSize = (int)(_vmdb.BlockSize * _vmdb.NumVBlks);
            if (buffer.Length < bufferSize)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = null;
                buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            }

            stream.ReadExactly(buffer, 0, bufferSize);

            _records = [];
            for (var i = 0; i < _vmdb.NumVBlks; ++i)
            {
                var rec = DatabaseRecord.ReadFrom(buffer, (int)(i * _vmdb.BlockSize));
                if (rec != null)
                {
                    _records.Add(rec.Id, rec);
                }
            }
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    internal IEnumerable<DiskRecord> Disks
    {
        get
        {
            foreach (var record in _records.Values)
            {
                if (record.RecordType == RecordType.Disk)
                {
                    yield return (DiskRecord)record;
                }
            }
        }
    }

    internal IEnumerable<VolumeRecord> Volumes
    {
        get
        {
            foreach (var record in _records.Values)
            {
                if (record.RecordType == RecordType.Volume)
                {
                    yield return (VolumeRecord)record;
                }
            }
        }
    }

    internal DiskGroupRecord GetDiskGroup(Guid guid)
    {
        foreach (var record in _records.Values)
        {
            if (record.RecordType == RecordType.DiskGroup)
            {
                var dgRecord = (DiskGroupRecord)record;
                if (new Guid(dgRecord.GroupGuidString) == guid || guid == Guid.Empty)
                {
                    return dgRecord;
                }
            }
        }

        return null;
    }

    internal IEnumerable<ComponentRecord> GetVolumeComponents(ulong volumeId)
    {
        foreach (var record in _records.Values)
        {
            if (record.RecordType == RecordType.Component)
            {
                var cmpntRecord = (ComponentRecord)record;
                if (cmpntRecord.VolumeId == volumeId)
                {
                    yield return cmpntRecord;
                }
            }
        }
    }

    internal IEnumerable<ExtentRecord> GetComponentExtents(ulong componentId)
    {
        foreach (var record in _records.Values)
        {
            if (record.RecordType == RecordType.Extent)
            {
                var extentRecord = (ExtentRecord)record;
                if (extentRecord.ComponentId == componentId)
                {
                    yield return extentRecord;
                }
            }
        }
    }

    internal DiskRecord GetDisk(ulong diskId)
    {
        return (DiskRecord)_records[diskId];
    }

    internal VolumeRecord GetVolume(ulong volumeId)
    {
        return (VolumeRecord)_records[volumeId];
    }

    internal VolumeRecord GetVolume(Guid id)
    {
        return FindRecord<VolumeRecord>(r => r.VolumeGuid == id, RecordType.Volume);
    }

    internal IEnumerable<VolumeRecord> GetVolumes()
    {
        foreach (var record in _records.Values)
        {
            if (record.RecordType == RecordType.Volume)
            {
                yield return (VolumeRecord)record;
            }
        }
    }

    internal T FindRecord<T>(Predicate<T> pred, RecordType typeId)
        where T : DatabaseRecord
    {
        foreach (var record in _records.Values)
        {
            if (record.RecordType == typeId)
            {
                var t = (T)record;
                if (pred(t))
                {
                    return t;
                }
            }
        }

        return null;
    }
}