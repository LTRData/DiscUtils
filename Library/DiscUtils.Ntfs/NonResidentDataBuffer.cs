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
using Buffer = DiscUtils.Streams.Buffer;

namespace DiscUtils.Ntfs;

internal class NonResidentDataBuffer : Buffer, IMappedBuffer
{
    protected ClusterStream _activeStream;

    protected long _bytesPerCluster;
    protected INtfsContext _context;
    protected CookedDataRuns _cookedRuns;

    protected byte[] _ioBuffer;
    protected RawClusterStream _rawStream;

    public NonResidentDataBuffer(INtfsContext context, NonResidentAttributeRecord record)
        : this(context, new CookedDataRuns(record.DataRuns, record), false) {}

    public NonResidentDataBuffer(INtfsContext context, CookedDataRuns cookedRuns, bool isMft)
    {
        _context = context;
        _cookedRuns = cookedRuns;

        _rawStream = new RawClusterStream(_context, _cookedRuns, isMft);
        _activeStream = _rawStream;

        _bytesPerCluster = _context.BiosParameterBlock.BytesPerCluster;
        _ioBuffer = new byte[_bytesPerCluster];
    }

    public long VirtualClusterCount
    {
        get { return _cookedRuns.NextVirtualCluster; }
    }

    public override bool CanRead
    {
        get { return _context.RawStream.CanRead; }
    }

    public override bool CanWrite
    {
        get { return false; }
    }

    public override long Capacity
    {
        get { return VirtualClusterCount * _bytesPerCluster; }
    }

    public override IEnumerable<StreamExtent> Extents
    {
        get
        {
            var extents = new List<StreamExtent>();
            foreach (var range in _activeStream.StoredClusters)
            {
                extents.Add(new StreamExtent(range.Offset * _bytesPerCluster, range.Count * _bytesPerCluster));
            }

            return StreamExtent.Intersect(extents, new StreamExtent(0, Capacity));
        }
    }

    public override IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
    {
        return StreamExtent.Intersect(Extents, new StreamExtent(start, count));
    }

    public long MapPosition(long pos)
    {
        var vcn = pos / _bytesPerCluster;
        var dataRunIdx = _cookedRuns.FindDataRun(vcn, 0);

        if (_cookedRuns[dataRunIdx].IsSparse)
        {
            return -1;
        }

        return _cookedRuns[dataRunIdx].StartLcn * _bytesPerCluster +
               (pos - _cookedRuns[dataRunIdx].StartVcn * _bytesPerCluster);
    }

    public override int Read(long pos, byte[] buffer, int offset, int count)
    {
        if (!CanRead)
        {
            throw new IOException("Attempt to read from file not opened for read");
        }

        StreamUtilities.AssertBufferParameters(buffer, offset, count);

        // Limit read to length of attribute
        var totalToRead = (int)Math.Min(count, Capacity - pos);
        if (totalToRead <= 0)
        {
            return 0;
        }

        var focusPos = pos;
        while (focusPos < pos + totalToRead)
        {
            var vcn = focusPos / _bytesPerCluster;
            var remaining = pos + totalToRead - focusPos;
            var clusterOffset = focusPos - vcn * _bytesPerCluster;

            if (vcn * _bytesPerCluster != focusPos || remaining < _bytesPerCluster)
            {
                // Unaligned or short read
                _activeStream.ReadClusters(vcn, 1, _ioBuffer, 0);

                var toRead = (int)Math.Min(remaining, _bytesPerCluster - clusterOffset);

                System.Buffer.BlockCopy(_ioBuffer, (int)clusterOffset, buffer, (int)(offset + (focusPos - pos)), toRead);

                focusPos += toRead;
            }
            else
            {
                // Aligned, full cluster reads...
                var fullClusters = (int)(remaining / _bytesPerCluster);
                _activeStream.ReadClusters(vcn, fullClusters, buffer, (int)(offset + (focusPos - pos)));

                focusPos += fullClusters * _bytesPerCluster;
            }
        }

        return totalToRead;
    }

    public override void Write(long pos, byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override int Read(long pos, Span<byte> buffer)
    {
        if (!CanRead)
        {
            throw new IOException("Attempt to read from file not opened for read");
        }

        // Limit read to length of attribute
        var totalToRead = (int)Math.Min(buffer.Length, Capacity - pos);
        if (totalToRead <= 0)
        {
            return 0;
        }

        var focusPos = pos;
        while (focusPos < pos + totalToRead)
        {
            var vcn = focusPos / _bytesPerCluster;
            var remaining = pos + totalToRead - focusPos;
            var clusterOffset = focusPos - vcn * _bytesPerCluster;

            if (vcn * _bytesPerCluster != focusPos || remaining < _bytesPerCluster)
            {
                // Unaligned or short read
                _activeStream.ReadClusters(vcn, 1, _ioBuffer, 0);

                var toRead = (int)Math.Min(remaining, _bytesPerCluster - clusterOffset);

                _ioBuffer.AsSpan((int)clusterOffset, toRead).CopyTo(buffer.Slice((int)(focusPos - pos)));

                focusPos += toRead;
            }
            else
            {
                // Aligned, full cluster reads...
                var fullClusters = (int)(remaining / _bytesPerCluster);
                _activeStream.ReadClusters(vcn, fullClusters, buffer.Slice((int)(focusPos - pos)));

                focusPos += fullClusters * _bytesPerCluster;
            }
        }

        return totalToRead;
    }

    public override async ValueTask<int> ReadAsync(long pos, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!CanRead)
        {
            throw new IOException("Attempt to read from file not opened for read");
        }

        // Limit read to length of attribute
        var totalToRead = (int)Math.Min(buffer.Length, Capacity - pos);
        if (totalToRead <= 0)
        {
            return 0;
        }

        var focusPos = pos;
        while (focusPos < pos + totalToRead)
        {
            var vcn = focusPos / _bytesPerCluster;
            var remaining = pos + totalToRead - focusPos;
            var clusterOffset = focusPos - vcn * _bytesPerCluster;

            if (vcn * _bytesPerCluster != focusPos || remaining < _bytesPerCluster)
            {
                // Unaligned or short read
                await _activeStream.ReadClustersAsync(vcn, 1, _ioBuffer, cancellationToken).ConfigureAwait(false);

                var toRead = (int)Math.Min(remaining, _bytesPerCluster - clusterOffset);

                _ioBuffer.AsMemory((int)clusterOffset, toRead).CopyTo(buffer.Slice((int)(focusPos - pos)));

                focusPos += toRead;
            }
            else
            {
                // Aligned, full cluster reads...
                var fullClusters = (int)(remaining / _bytesPerCluster);
                await _activeStream.ReadClustersAsync(vcn, fullClusters, buffer.Slice((int)(focusPos - pos)), cancellationToken).ConfigureAwait(false);

                focusPos += fullClusters * _bytesPerCluster;
            }
        }

        return totalToRead;
    }

    public override void Write(long pos, ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException();

    public override ValueTask WriteAsync(long pos, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override void SetCapacity(long value)
    {
        throw new NotSupportedException();
    }

    public virtual ValueTask SetCapacityAsync(long value, CancellationToken cancellationToken)
    {
        SetCapacity(value);
        return default;
    }
}