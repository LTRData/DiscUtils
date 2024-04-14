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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;

namespace DiscUtils.Ntfs;

internal class NonResidentAttributeBuffer : NonResidentDataBuffer
{
    private readonly NtfsAttribute _attribute;
    private readonly File _file;

    public NonResidentAttributeBuffer(File file, NtfsAttribute attribute)
        : base(file.Context, CookRuns(attribute), file.IndexInMft == MasterFileTable.MftIndex)
    {
        _file = file;
        _attribute = attribute;

        _activeStream = (attribute.Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) switch
        {
            AttributeFlags.Sparse => new SparseClusterStream(_attribute, _rawStream),
            AttributeFlags.Compressed => new CompressedClusterStream(_context, _attribute, _rawStream),
            AttributeFlags.None => _rawStream,
            _ => throw new NotImplementedException($"Unhandled attribute type '{attribute.Flags}'"),
        };
    }

    public override bool CanWrite => _context.RawStream.CanWrite && _file != null;

    public override long Capacity => PrimaryAttributeRecord.DataLength;

    private NonResidentAttributeRecord PrimaryAttributeRecord => _attribute.PrimaryRecord as NonResidentAttributeRecord;

    public void AlignVirtualClusterCount()
    {
        _file.MarkMftRecordDirty();
        _activeStream.ExpandToClusters(MathUtilities.Ceil(_attribute.Length, _bytesPerCluster),
            (NonResidentAttributeRecord)_attribute.LastExtent, false);
    }

    public override void SetCapacity(long value)
    {
        if (!CanWrite)
        {
            throw new IOException("Attempt to change length of file not opened for write");
        }

        if (value == Capacity)
        {
            return;
        }

        _file.MarkMftRecordDirty();

        var newClusterCount = MathUtilities.Ceil(value, _bytesPerCluster);

        if (value < Capacity)
        {
            Truncate(value);
        }
        else
        {
            _activeStream.ExpandToClusters(newClusterCount, (NonResidentAttributeRecord)_attribute.LastExtent, true);

            PrimaryAttributeRecord.AllocatedLength = _cookedRuns.NextVirtualCluster * _bytesPerCluster;
        }

        PrimaryAttributeRecord.DataLength = value;

        if (PrimaryAttributeRecord.InitializedDataLength > value)
        {
            PrimaryAttributeRecord.InitializedDataLength = value;
        }

        _cookedRuns.CollapseRuns();
    }

    public override async ValueTask SetCapacityAsync(long value, CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            throw new IOException("Attempt to change length of file not opened for write");
        }

        if (value == Capacity)
        {
            return;
        }

        _file.MarkMftRecordDirty();

        var newClusterCount = MathUtilities.Ceil(value, _bytesPerCluster);

        if (value < Capacity)
        {
            Truncate(value);
        }
        else
        {
            await _activeStream.ExpandToClustersAsync(newClusterCount, (NonResidentAttributeRecord)_attribute.LastExtent, true, cancellationToken).ConfigureAwait(false);

            PrimaryAttributeRecord.AllocatedLength = _cookedRuns.NextVirtualCluster * _bytesPerCluster;
        }

        PrimaryAttributeRecord.DataLength = value;

        if (PrimaryAttributeRecord.InitializedDataLength > value)
        {
            PrimaryAttributeRecord.InitializedDataLength = value;
        }

        _cookedRuns.CollapseRuns();
    }

    public override void Write(long pos, byte[] buffer, int offset, int count)
    {
        if (!CanWrite)
        {
            throw new IOException("Attempt to write to file not opened for write");
        }

        if (count == 0)
        {
            return;
        }

        if (pos + count > Capacity)
        {
            SetCapacity(pos + count);
        }

        // Write zeros from end of current initialized data to the start of the new write
        if (pos > PrimaryAttributeRecord.InitializedDataLength)
        {
            InitializeData(pos);
        }

        var allocatedClusters = 0;

        var focusPos = pos;
        while (focusPos < pos + count)
        {
            var vcn = focusPos / _bytesPerCluster;
            var remaining = pos + count - focusPos;
            var clusterOffset = focusPos - vcn * _bytesPerCluster;

            if (vcn * _bytesPerCluster != focusPos || remaining < _bytesPerCluster)
            {
                // Unaligned or short write
                var toWrite = (int)Math.Min(remaining, _bytesPerCluster - clusterOffset);

                _activeStream.ReadClusters(vcn, 1, _ioBuffer, 0);
                System.Buffer.BlockCopy(buffer, (int)(offset + (focusPos - pos)), _ioBuffer, (int)clusterOffset, toWrite);
                allocatedClusters += _activeStream.WriteClusters(vcn, 1, _ioBuffer, 0);

                focusPos += toWrite;
            }
            else
            {
                // Aligned, full cluster writes...
                var fullClusters = (int)(remaining / _bytesPerCluster);
                allocatedClusters += _activeStream.WriteClusters(vcn, fullClusters, buffer,
                    (int)(offset + (focusPos - pos)));

                focusPos += fullClusters * _bytesPerCluster;
            }
        }

        if (pos + count > PrimaryAttributeRecord.InitializedDataLength)
        {
            _file.MarkMftRecordDirty();

            PrimaryAttributeRecord.InitializedDataLength = pos + count;
        }

        if (pos + count > PrimaryAttributeRecord.DataLength)
        {
            _file.MarkMftRecordDirty();

            PrimaryAttributeRecord.DataLength = pos + count;
        }

        if ((_attribute.Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0)
        {
            PrimaryAttributeRecord.CompressedDataSize += allocatedClusters * _bytesPerCluster;
        }

        _cookedRuns.CollapseRuns();
    }

    public override void Write(long pos, ReadOnlySpan<byte> buffer)
    {
        if (!CanWrite)
        {
            throw new IOException("Attempt to write to file not opened for write");
        }

        if (buffer.IsEmpty)
        {
            return;
        }

        if (pos + buffer.Length > Capacity)
        {
            SetCapacity(pos + buffer.Length);
        }

        // Write zeros from end of current initialized data to the start of the new write
        if (pos > PrimaryAttributeRecord.InitializedDataLength)
        {
            InitializeData(pos);
        }

        var allocatedClusters = 0;

        var focusPos = pos;
        while (focusPos < pos + buffer.Length)
        {
            var vcn = focusPos / _bytesPerCluster;
            var remaining = pos + buffer.Length - focusPos;
            var clusterOffset = focusPos - vcn * _bytesPerCluster;

            if (vcn * _bytesPerCluster != focusPos || remaining < _bytesPerCluster)
            {
                // Unaligned or short write
                var toWrite = (int)Math.Min(remaining, _bytesPerCluster - clusterOffset);

                _activeStream.ReadClusters(vcn, 1, _ioBuffer, 0);
                buffer.Slice((int)(focusPos - pos), toWrite).CopyTo(_ioBuffer.AsSpan((int)clusterOffset));
                allocatedClusters += _activeStream.WriteClusters(vcn, 1, _ioBuffer, 0);

                focusPos += toWrite;
            }
            else
            {
                // Aligned, full cluster writes...
                var fullClusters = (int)(remaining / _bytesPerCluster);
                allocatedClusters += _activeStream.WriteClusters(vcn, fullClusters,
                    buffer.Slice((int)(focusPos - pos)));

                focusPos += fullClusters * _bytesPerCluster;
            }
        }

        if (pos + buffer.Length > PrimaryAttributeRecord.InitializedDataLength)
        {
            _file.MarkMftRecordDirty();

            PrimaryAttributeRecord.InitializedDataLength = pos + buffer.Length;
        }

        if (pos + buffer.Length > PrimaryAttributeRecord.DataLength)
        {
            _file.MarkMftRecordDirty();

            PrimaryAttributeRecord.DataLength = pos + buffer.Length;
        }

        if ((_attribute.Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0)
        {
            PrimaryAttributeRecord.CompressedDataSize += allocatedClusters * _bytesPerCluster;
        }

        _cookedRuns.CollapseRuns();
    }

    public override async ValueTask WriteAsync(long pos, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            throw new IOException("Attempt to write to file not opened for write");
        }

        if (buffer.IsEmpty)
        {
            return;
        }

        if (pos + buffer.Length > Capacity)
        {
            await SetCapacityAsync(pos + buffer.Length, cancellationToken).ConfigureAwait(false);
        }

        // Write zeros from end of current initialized data to the start of the new write
        if (pos > PrimaryAttributeRecord.InitializedDataLength)
        {
            await InitializeDataAsync(pos, cancellationToken).ConfigureAwait(false);
        }

        var allocatedClusters = 0;

        var focusPos = pos;
        while (focusPos < pos + buffer.Length)
        {
            var vcn = focusPos / _bytesPerCluster;
            var remaining = pos + buffer.Length - focusPos;
            var clusterOffset = focusPos - vcn * _bytesPerCluster;

            if (vcn * _bytesPerCluster != focusPos || remaining < _bytesPerCluster)
            {
                // Unaligned or short write
                var toWrite = (int)Math.Min(remaining, _bytesPerCluster - clusterOffset);

                await _activeStream.ReadClustersAsync(vcn, 1, _ioBuffer, cancellationToken).ConfigureAwait(false);
                buffer.Slice((int)(focusPos - pos), toWrite).CopyTo(_ioBuffer.AsMemory((int)clusterOffset));
                allocatedClusters += _activeStream.WriteClusters(vcn, 1, _ioBuffer, 0);

                focusPos += toWrite;
            }
            else
            {
                // Aligned, full cluster writes...
                var fullClusters = (int)(remaining / _bytesPerCluster);
                allocatedClusters += await _activeStream.WriteClustersAsync(vcn, fullClusters,
                    buffer.Slice((int)(focusPos - pos)), cancellationToken).ConfigureAwait(false);

                focusPos += fullClusters * _bytesPerCluster;
            }
        }

        if (pos + buffer.Length > PrimaryAttributeRecord.InitializedDataLength)
        {
            _file.MarkMftRecordDirty();

            PrimaryAttributeRecord.InitializedDataLength = pos + buffer.Length;
        }

        if (pos + buffer.Length > PrimaryAttributeRecord.DataLength)
        {
            _file.MarkMftRecordDirty();

            PrimaryAttributeRecord.DataLength = pos + buffer.Length;
        }

        if ((_attribute.Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0)
        {
            PrimaryAttributeRecord.CompressedDataSize += allocatedClusters * _bytesPerCluster;
        }

        _cookedRuns.CollapseRuns();
    }

    public override void Clear(long pos, int count)
    {
        if (!CanWrite)
        {
            throw new IOException("Attempt to erase bytes from file not opened for write");
        }

        if (count == 0)
        {
            return;
        }

        if (pos + count > Capacity)
        {
            SetCapacity(pos + count);
        }

        _file.MarkMftRecordDirty();

        // Write zeros from end of current initialized data to the start of the new write
        if (pos > PrimaryAttributeRecord.InitializedDataLength)
        {
            InitializeData(pos);
        }

        var releasedClusters = 0;

        var focusPos = pos;
        while (focusPos < pos + count)
        {
            var vcn = focusPos / _bytesPerCluster;
            var remaining = pos + count - focusPos;
            var clusterOffset = focusPos - vcn * _bytesPerCluster;

            if (vcn * _bytesPerCluster != focusPos || remaining < _bytesPerCluster)
            {
                // Unaligned or short write
                var toClear = (int)Math.Min(remaining, _bytesPerCluster - clusterOffset);

                if (_activeStream.IsClusterStored(vcn))
                {
                    _activeStream.ReadClusters(vcn, 1, _ioBuffer, 0);
                    Array.Clear(_ioBuffer, (int)clusterOffset, toClear);
                    releasedClusters -= _activeStream.WriteClusters(vcn, 1, _ioBuffer, 0);
                }

                focusPos += toClear;
            }
            else
            {
                // Aligned, full cluster clears...
                var fullClusters = (int)(remaining / _bytesPerCluster);
                releasedClusters += _activeStream.ClearClusters(vcn, fullClusters);

                focusPos += fullClusters * _bytesPerCluster;
            }
        }

        if (pos + count > PrimaryAttributeRecord.InitializedDataLength)
        {
            PrimaryAttributeRecord.InitializedDataLength = pos + count;
        }

        if (pos + count > PrimaryAttributeRecord.DataLength)
        {
            PrimaryAttributeRecord.DataLength = pos + count;
        }

        if ((_attribute.Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0)
        {
            PrimaryAttributeRecord.CompressedDataSize -= releasedClusters * _bytesPerCluster;
        }

        _cookedRuns.CollapseRuns();
    }

    private static CookedDataRuns CookRuns(NtfsAttribute attribute)
    {
        var result = new CookedDataRuns();

        foreach (NonResidentAttributeRecord record in attribute.Records)
        {
            if (record.StartVcn != result.NextVirtualCluster)
            {
                throw new IOException("Invalid NTFS attribute - non-contiguous data runs");
            }

            result.Append(record.DataRuns, record);
        }

        return result;
    }

    private void InitializeData(long pos)
    {
        var initDataLen = PrimaryAttributeRecord.InitializedDataLength;
        _file.MarkMftRecordDirty();

        var clustersAllocated = 0;

        while (initDataLen < pos)
        {
            var vcn = initDataLen / _bytesPerCluster;
            if (initDataLen % _bytesPerCluster != 0 || pos - initDataLen < _bytesPerCluster)
            {
                var clusterOffset = (int)(initDataLen - vcn * _bytesPerCluster);
                var toClear = (int)Math.Min(_bytesPerCluster - clusterOffset, pos - initDataLen);

                if (_activeStream.IsClusterStored(vcn))
                {
                    _activeStream.ReadClusters(vcn, 1, _ioBuffer, 0);
                    Array.Clear(_ioBuffer, clusterOffset, toClear);
                    clustersAllocated += _activeStream.WriteClusters(vcn, 1, _ioBuffer, 0);
                }

                initDataLen += toClear;
            }
            else
            {
                var numClusters = (int)(pos / _bytesPerCluster - vcn);
                clustersAllocated -= _activeStream.ClearClusters(vcn, numClusters);

                initDataLen += numClusters * _bytesPerCluster;
            }
        }

        PrimaryAttributeRecord.InitializedDataLength = pos;

        if ((_attribute.Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0)
        {
            PrimaryAttributeRecord.CompressedDataSize += clustersAllocated * _bytesPerCluster;
        }
    }

    private async ValueTask InitializeDataAsync(long pos, CancellationToken cancellationToken)
    {
        var initDataLen = PrimaryAttributeRecord.InitializedDataLength;
        _file.MarkMftRecordDirty();

        var clustersAllocated = 0;

        while (initDataLen < pos)
        {
            var vcn = initDataLen / _bytesPerCluster;
            if (initDataLen % _bytesPerCluster != 0 || pos - initDataLen < _bytesPerCluster)
            {
                var clusterOffset = (int)(initDataLen - vcn * _bytesPerCluster);
                var toClear = (int)Math.Min(_bytesPerCluster - clusterOffset, pos - initDataLen);

                if (_activeStream.IsClusterStored(vcn))
                {
                    await _activeStream.ReadClustersAsync(vcn, 1, _ioBuffer, cancellationToken).ConfigureAwait(false);
                    Array.Clear(_ioBuffer, clusterOffset, toClear);
                    clustersAllocated += await _activeStream.WriteClustersAsync(vcn, 1, _ioBuffer, cancellationToken).ConfigureAwait(false);
                }

                initDataLen += toClear;
            }
            else
            {
                var numClusters = (int)(pos / _bytesPerCluster - vcn);
                clustersAllocated -= await _activeStream.ClearClustersAsync(vcn, numClusters, cancellationToken).ConfigureAwait(false);

                initDataLen += numClusters * _bytesPerCluster;
            }
        }

        PrimaryAttributeRecord.InitializedDataLength = pos;

        if ((_attribute.Flags & (AttributeFlags.Compressed | AttributeFlags.Sparse)) != 0)
        {
            PrimaryAttributeRecord.CompressedDataSize += clustersAllocated * _bytesPerCluster;
        }
    }

    private void Truncate(long value)
    {
        var endVcn = MathUtilities.Ceil(value, _bytesPerCluster);

        // Release the clusters
        _activeStream.TruncateToClusters(endVcn);

        // First, remove any extents that are now redundant.
        var extentCache =
            new Dictionary<AttributeReference, AttributeRecord>(_attribute.Extents);
        foreach (var extent in extentCache)
        {
            if (extent.Value.StartVcn >= endVcn)
            {
                var record = (NonResidentAttributeRecord)extent.Value;
                _file.RemoveAttributeExtent(extent.Key);
                _attribute.RemoveExtentCacheSafe(extent.Key);
            }
        }

        PrimaryAttributeRecord.LastVcn = Math.Max(0, endVcn - 1);
        PrimaryAttributeRecord.AllocatedLength = endVcn * _bytesPerCluster;
        PrimaryAttributeRecord.DataLength = value;
        PrimaryAttributeRecord.InitializedDataLength = Math.Min(PrimaryAttributeRecord.InitializedDataLength, value);

        _file.MarkMftRecordDirty();
    }
}