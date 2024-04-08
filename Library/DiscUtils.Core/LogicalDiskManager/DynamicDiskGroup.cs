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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DiscUtils.Partitions;
using DiscUtils.Streams;

namespace DiscUtils.LogicalDiskManager;

internal class DynamicDiskGroup : IDiagnosticTraceable
{
    private readonly Database _database;
    private readonly Dictionary<Guid, DynamicDisk> _disks;
    private readonly DiskGroupRecord _record;

    internal DynamicDiskGroup(VirtualDisk disk)
    {
        _disks = new Dictionary<Guid, DynamicDisk>();

        var dynDisk = new DynamicDisk(disk);
        _database = dynDisk.Database;
        _disks.Add(dynDisk.Id, dynDisk);
        _record = dynDisk.Database.GetDiskGroup(dynDisk.GroupId);
    }

    #region IDiagnosticTraceable Members

    public void Dump(TextWriter writer, string linePrefix)
    {
        writer.WriteLine($"{linePrefix}DISK GROUP ({_record.Name})");
        writer.WriteLine($"{linePrefix}  Name: {_record.Name}");
        writer.WriteLine($"{linePrefix}  Flags: 0x{_record.Flags & 0xFFF0:X4}");
        writer.WriteLine($"{linePrefix}  Database Id: {_record.Id}");
        writer.WriteLine($"{linePrefix}  Guid: {_record.GroupGuidString}");
        writer.WriteLine();

        writer.WriteLine($"{linePrefix}  DISKS");
        foreach (var disk in _database.Disks)
        {
            writer.WriteLine($"{linePrefix}    DISK ({disk.Name})");
            writer.WriteLine($"{linePrefix}      Name: {disk.Name}");
            writer.WriteLine($"{linePrefix}      Flags: 0x{disk.Flags & 0xFFF0:X4}");
            writer.WriteLine($"{linePrefix}      Database Id: {disk.Id}");
            writer.WriteLine($"{linePrefix}      Guid: {disk.DiskGuidString}");

            if (_disks.TryGetValue(new Guid(disk.DiskGuidString), out var dynDisk))
            {
                writer.WriteLine($"{linePrefix}      PRIVATE HEADER");
                dynDisk.Dump(writer, $"{linePrefix}        ");
            }
        }

        writer.WriteLine($"{linePrefix}  VOLUMES");
        foreach (var vol in _database.Volumes)
        {
            writer.WriteLine($"{linePrefix}    VOLUME ({vol.Name})");
            writer.WriteLine($"{linePrefix}      Name: {vol.Name}");
            writer.WriteLine($"{linePrefix}      BIOS Type: {vol.BiosType.ToString("X2")} [{BiosPartitionTypes.ToString(vol.BiosType)}]");
            writer.WriteLine($"{linePrefix}      Flags: 0x{vol.Flags & 0xFFF0:X4}");
            writer.WriteLine($"{linePrefix}      Database Id: {vol.Id}");
            writer.WriteLine($"{linePrefix}      Guid: {vol.VolumeGuid}");
            writer.WriteLine($"{linePrefix}      State: {vol.ActiveString}");
            writer.WriteLine($"{linePrefix}      Drive Hint: {vol.MountHint}");
            writer.WriteLine($"{linePrefix}      Num Components: {vol.ComponentCount}");
            writer.WriteLine($"{linePrefix}      Link Id: {vol.PartitionComponentLink}");

            writer.WriteLine($"{linePrefix}      COMPONENTS");
            foreach (var cmpnt in _database.GetVolumeComponents(vol.Id))
            {
                writer.WriteLine($"{linePrefix}        COMPONENT ({cmpnt.Name})");
                writer.WriteLine($"{linePrefix}          Name: {cmpnt.Name}");
                writer.WriteLine($"{linePrefix}          Flags: 0x{cmpnt.Flags & 0xFFF0:X4}");
                writer.WriteLine($"{linePrefix}          Database Id: {cmpnt.Id}");
                writer.WriteLine($"{linePrefix}          State: {cmpnt.StatusString}");
                writer.WriteLine($"{linePrefix}          Mode: {cmpnt.MergeType}");
                writer.WriteLine($"{linePrefix}          Num Extents: {cmpnt.NumExtents}");
                writer.WriteLine($"{linePrefix}          Link Id: {cmpnt.LinkId}");
                writer.WriteLine($"{linePrefix}          Stripe Size: {cmpnt.StripeSizeSectors} (Sectors)");
                writer.WriteLine($"{linePrefix}          Stripe Stride: {cmpnt.StripeStride}");

                writer.WriteLine($"{linePrefix}          EXTENTS");
                foreach (var extent in _database.GetComponentExtents(cmpnt.Id))
                {
                    writer.WriteLine($"{linePrefix}            EXTENT ({extent.Name})");
                    writer.WriteLine($"{linePrefix}              Name: {extent.Name}");
                    writer.WriteLine($"{linePrefix}              Flags: 0x{extent.Flags & 0xFFF0:X4}");
                    writer.WriteLine($"{linePrefix}              Database Id: {extent.Id}");
                    writer.WriteLine($"{linePrefix}              Disk Offset: {extent.DiskOffsetLba} (Sectors)");
                    writer.WriteLine($"{linePrefix}              Volume Offset: {extent.OffsetInVolumeLba} (Sectors)");
                    writer.WriteLine($"{linePrefix}              Size: {extent.SizeLba} (Sectors)");
                    writer.WriteLine($"{linePrefix}              Component Id: {extent.ComponentId}");
                    writer.WriteLine($"{linePrefix}              Disk Id: {extent.DiskId}");
                    writer.WriteLine($"{linePrefix}              Link Id: {extent.PartitionComponentLink}");
                    writer.WriteLine($"{linePrefix}              Interleave Order: {extent.InterleaveOrder}");
                }
            }
        }
    }

    #endregion

    public void Add(VirtualDisk disk)
    {
        var dynDisk = new DynamicDisk(disk);
        _disks.Add(dynDisk.Id, dynDisk);
    }

    internal IEnumerable<DynamicVolume> GetVolumes()
    {
        foreach (var record in _database.GetVolumes())
        {
            yield return new DynamicVolume(this, record.VolumeGuid);
        }
    }

    internal VolumeRecord GetVolume(Guid volume)
    {
        return _database.GetVolume(volume);
    }

    internal LogicalVolumeStatus GetVolumeStatus(ulong volumeId)
    {
        return GetVolumeStatus(_database.GetVolume(volumeId));
    }

    internal SparseStream OpenVolume(ulong volumeId)
    {
        return OpenVolume(_database.GetVolume(volumeId));
    }

    private static int CompareExtentOffsets(ExtentRecord x, ExtentRecord y)
    {
        if (x.OffsetInVolumeLba > y.OffsetInVolumeLba)
        {
            return 1;
        }

        if (x.OffsetInVolumeLba < y.OffsetInVolumeLba)
        {
            return -1;
        }

        return 0;
    }

    private static int CompareExtentInterleaveOrder(ExtentRecord x, ExtentRecord y)
    {
        if (x.InterleaveOrder > y.InterleaveOrder)
        {
            return 1;
        }

        if (x.InterleaveOrder < y.InterleaveOrder)
        {
            return -1;
        }

        return 0;
    }

    private static LogicalVolumeStatus WorstOf(LogicalVolumeStatus x, LogicalVolumeStatus y)
    {
        return (LogicalVolumeStatus)Math.Max((int)x, (int)y);
    }

    private LogicalVolumeStatus GetVolumeStatus(VolumeRecord volume)
    {
        var numFailed = 0;
        ulong numOK = 0;
        var worst = LogicalVolumeStatus.Healthy;
        foreach (var cmpnt in _database.GetVolumeComponents(volume.Id))
        {
            var cmpntStatus = GetComponentStatus(cmpnt);
            worst = WorstOf(worst, cmpntStatus);
            if (cmpntStatus == LogicalVolumeStatus.Failed)
            {
                numFailed++;
            }
            else
            {
                numOK++;
            }
        }

        if (numOK < 1)
        {
            return LogicalVolumeStatus.Failed;
        }

        if (numOK == volume.ComponentCount)
        {
            return worst;
        }

        return LogicalVolumeStatus.FailedRedundancy;
    }

    private LogicalVolumeStatus GetComponentStatus(ComponentRecord cmpnt)
    {
        // NOTE: no support for RAID, so either valid or failed...
        var status = LogicalVolumeStatus.Healthy;

        foreach (var extent in _database.GetComponentExtents(cmpnt.Id))
        {
            var disk = _database.GetDisk(extent.DiskId);
            if (!_disks.ContainsKey(new Guid(disk.DiskGuidString)))
            {
                status = LogicalVolumeStatus.Failed;
                break;
            }
        }

        return status;
    }

    private SparseStream OpenExtent(ExtentRecord extent)
    {
        var disk = _database.GetDisk(extent.DiskId);

        var diskObj = _disks[new Guid(disk.DiskGuidString)];

        return new SubStream(diskObj.Content, Ownership.None,
            (diskObj.DataOffset + extent.DiskOffsetLba) * Sizes.Sector, extent.SizeLba * Sizes.Sector);
    }

    private SparseStream OpenComponent(ComponentRecord component)
    {
        if (component.MergeType == ExtentMergeType.Concatenated)
        {
            var extents = new List<ExtentRecord>(_database.GetComponentExtents(component.Id));
            extents.Sort(CompareExtentOffsets);

            // Sanity Check...
            long pos = 0;
            foreach (var extent in extents)
            {
                if (extent.OffsetInVolumeLba != pos)
                {
                    throw new IOException("Volume extents are non-contiguous");
                }

                pos += extent.SizeLba;
            }

            var streams = new List<SparseStream>();
            foreach (var extent in extents)
            {
                streams.Add(OpenExtent(extent));
            }

            return new ConcatStream(Ownership.Dispose, streams);
        }

        if (component.MergeType == ExtentMergeType.Interleaved)
        {
            var extents = new List<ExtentRecord>(_database.GetComponentExtents(component.Id));
            extents.Sort(CompareExtentInterleaveOrder);

            var streams = new List<SparseStream>();
            foreach (var extent in extents)
            {
                streams.Add(OpenExtent(extent));
            }

            return new StripedStream(component.StripeSizeSectors * Sizes.Sector, Ownership.Dispose, streams);
        }

        throw new NotImplementedException($"Unknown component mode: {component.MergeType}");
    }

    private SparseStream OpenVolume(VolumeRecord volume)
    {
        var cmpntStreams = new List<SparseStream>();
        foreach (var component in _database.GetVolumeComponents(volume.Id))
        {
            if (GetComponentStatus(component) == LogicalVolumeStatus.Healthy)
            {
                cmpntStreams.Add(OpenComponent(component));
            }
        }

        if (cmpntStreams.Count < 1)
        {
            throw new IOException("Volume with no associated or healthy components");
        }

        if (cmpntStreams.Count == 1)
        {
            return cmpntStreams[0];
        }

        return new MirrorStream(Ownership.Dispose, cmpntStreams);
    }
}