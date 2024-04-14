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
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Ntfs;

/// <summary>
/// Low-level non-resident attribute operations.
/// </summary>
/// <remarks>
/// Responsible for:
/// * Cluster Allocation / Release
/// * Reading clusters from disk
/// * Writing clusters to disk
/// * Substituting zeros for 'sparse'/'unallocated' clusters
/// Not responsible for:
/// * Compression / Decompression
/// * Extending attributes.
/// </remarks>
internal sealed class RawClusterStream : ClusterStream
{
    private readonly int _bytesPerCluster;
    private readonly INtfsContext _context;
    private readonly CookedDataRuns _cookedRuns;
    private readonly Stream _fsStream;
    private readonly bool _isMft;

    public RawClusterStream(INtfsContext context, CookedDataRuns cookedRuns, bool isMft)
    {
        _context = context;
        _cookedRuns = cookedRuns;
        _isMft = isMft;

        _fsStream = _context.RawStream;
        _bytesPerCluster = context.BiosParameterBlock.BytesPerCluster;
    }

    public override long AllocatedClusterCount
    {
        get
        {
            long total = 0;
            for (var i = 0; i < _cookedRuns.Count; ++i)
            {
                var run = _cookedRuns[i];
                total += run.IsSparse ? 0 : run.Length;
            }

            return total;
        }
    }

    public override IEnumerable<Range<long, long>> StoredClusters
    {
        get
        {
            Range<long, long> lastVcnRange = default;
            var ranges = new List<Range<long, long>>();

            var runCount = _cookedRuns.Count;
            for (var i = 0; i < runCount; i++)
            {
                var cookedRun = _cookedRuns[i];
                if (!cookedRun.IsSparse)
                {
                    var startPos = cookedRun.StartVcn;
                    if (lastVcnRange != default && lastVcnRange.Offset + lastVcnRange.Count == startPos)
                    {
                        lastVcnRange = new Range<long, long>(lastVcnRange.Offset,
                            lastVcnRange.Count + cookedRun.Length);
                        ranges[ranges.Count - 1] = lastVcnRange;
                    }
                    else
                    {
                        lastVcnRange = new Range<long, long>(cookedRun.StartVcn, cookedRun.Length);
                        ranges.Add(lastVcnRange);
                    }
                }
            }

            return ranges;
        }
    }

    public override bool IsClusterStored(long vcn)
    {
        var runIdx = _cookedRuns.FindDataRun(vcn, 0);
        return !_cookedRuns[runIdx].IsSparse;
    }

    public bool AreAllClustersStored(long vcn, int count)
    {
        var runIdx = 0;
        var focusVcn = vcn;
        while (focusVcn < vcn + count)
        {
            runIdx = _cookedRuns.FindDataRun(focusVcn, runIdx);

            var run = _cookedRuns[runIdx];
            if (run.IsSparse)
            {
                return false;
            }

            focusVcn = run.StartVcn + run.Length;
        }

        return true;
    }

    public override void ExpandToClusters(long numVirtualClusters, NonResidentAttributeRecord extent, bool allocate)
    {
        var totalVirtualClusters = _cookedRuns.NextVirtualCluster;
        if (totalVirtualClusters < numVirtualClusters)
        {
            var realExtent = extent ?? _cookedRuns.Last.AttributeExtent;
            var newRun = new DataRun(0, numVirtualClusters - totalVirtualClusters, true);
            realExtent.DataRuns.Add(newRun);
            _cookedRuns.Append(newRun, extent);
            realExtent.LastVcn = numVirtualClusters - 1;
        }

        if (allocate)
        {
            AllocateClusters(totalVirtualClusters, (int)(numVirtualClusters - totalVirtualClusters));
        }
    }

    public override async ValueTask ExpandToClustersAsync(long numVirtualClusters, NonResidentAttributeRecord extent, bool allocate, CancellationToken cancellationToken)
    {
        var totalVirtualClusters = _cookedRuns.NextVirtualCluster;
        if (totalVirtualClusters < numVirtualClusters)
        {
            var realExtent = extent ?? _cookedRuns.Last.AttributeExtent;
            var newRun = new DataRun(0, numVirtualClusters - totalVirtualClusters, true);
            realExtent.DataRuns.Add(newRun);
            _cookedRuns.Append(newRun, extent);
            realExtent.LastVcn = numVirtualClusters - 1;
        }

        if (allocate)
        {
            await AllocateClustersAsync(totalVirtualClusters, (int)(numVirtualClusters - totalVirtualClusters), cancellationToken).ConfigureAwait(false);
        }
    }

    public override void TruncateToClusters(long numVirtualClusters)
    {
        if (numVirtualClusters < _cookedRuns.NextVirtualCluster)
        {
            ReleaseClusters(numVirtualClusters, (int)(_cookedRuns.NextVirtualCluster - numVirtualClusters));

            var runIdx = _cookedRuns.FindDataRun(numVirtualClusters, 0);

            if (numVirtualClusters != _cookedRuns[runIdx].StartVcn)
            {
                _cookedRuns.SplitRun(runIdx, numVirtualClusters);
                runIdx++;
            }

            _cookedRuns.TruncateAt(runIdx);
        }
    }

    public int AllocateClusters(long startVcn, int count)
    {
        if (startVcn + count > _cookedRuns.NextVirtualCluster)
        {
            throw new IOException("Attempt to allocate unknown clusters");
        }

        var totalAllocated = 0;
        var runIdx = 0;

        var focus = startVcn;
        while (focus < startVcn + count)
        {
            runIdx = _cookedRuns.FindDataRun(focus, runIdx);
            var run = _cookedRuns[runIdx];

            if (run.IsSparse)
            {
                if (focus != run.StartVcn)
                {
                    _cookedRuns.SplitRun(runIdx, focus);
                    runIdx++;
                    run = _cookedRuns[runIdx];
                }

                var numClusters = Math.Min(startVcn + count - focus, run.Length);
                if (numClusters != run.Length)
                {
                    _cookedRuns.SplitRun(runIdx, focus + numClusters);
                    run = _cookedRuns[runIdx];
                }

                long nextCluster = -1;
                for (var i = runIdx - 1; i >= 0; --i)
                {
                    if (!_cookedRuns[i].IsSparse)
                    {
                        nextCluster = _cookedRuns[i].StartLcn + _cookedRuns[i].Length;
                        break;
                    }
                }

                var alloced = _context.ClusterBitmap.AllocateClusters(numClusters, nextCluster, _isMft,
                                                          AllocatedClusterCount);

                var runs = new List<DataRun>();

                var lcn = runIdx == 0 ? 0 : _cookedRuns[runIdx - 1].StartLcn;
                foreach (var allocation in alloced)
                {
                    runs.Add(new DataRun(allocation.Offset - lcn, allocation.Count, false));
                    lcn = allocation.Offset;
                }

                _cookedRuns.MakeNonSparse(runIdx, runs);

                totalAllocated += (int)numClusters;
                focus += numClusters;
            }
            else
            {
                focus = run.StartVcn + run.Length;
            }
        }

        return totalAllocated;
    }

    public async ValueTask<int> AllocateClustersAsync(long startVcn, int count, CancellationToken cancellationToken)
    {
        if (startVcn + count > _cookedRuns.NextVirtualCluster)
        {
            throw new IOException("Attempt to allocate unknown clusters");
        }

        var totalAllocated = 0;
        var runIdx = 0;

        var focus = startVcn;
        while (focus < startVcn + count)
        {
            runIdx = _cookedRuns.FindDataRun(focus, runIdx);
            var run = _cookedRuns[runIdx];

            if (run.IsSparse)
            {
                if (focus != run.StartVcn)
                {
                    _cookedRuns.SplitRun(runIdx, focus);
                    runIdx++;
                    run = _cookedRuns[runIdx];
                }

                var numClusters = Math.Min(startVcn + count - focus, run.Length);
                if (numClusters != run.Length)
                {
                    _cookedRuns.SplitRun(runIdx, focus + numClusters);
                    run = _cookedRuns[runIdx];
                }

                long nextCluster = -1;
                for (var i = runIdx - 1; i >= 0; --i)
                {
                    if (!_cookedRuns[i].IsSparse)
                    {
                        nextCluster = _cookedRuns[i].StartLcn + _cookedRuns[i].Length;
                        break;
                    }
                }

                var alloced = await _context.ClusterBitmap.AllocateClustersAsync(numClusters, nextCluster, _isMft,
                                                          AllocatedClusterCount, cancellationToken).ConfigureAwait(false);

                var runs = new List<DataRun>();

                var lcn = runIdx == 0 ? 0 : _cookedRuns[runIdx - 1].StartLcn;
                foreach (var allocation in alloced)
                {
                    runs.Add(new DataRun(allocation.Offset - lcn, allocation.Count, false));
                    lcn = allocation.Offset;
                }

                _cookedRuns.MakeNonSparse(runIdx, runs);

                totalAllocated += (int)numClusters;
                focus += numClusters;
            }
            else
            {
                focus = run.StartVcn + run.Length;
            }
        }

        return totalAllocated;
    }

    public int ReleaseClusters(long startVcn, int count)
    {
        var runIdx = 0;

        var totalReleased = 0;

        var focus = startVcn;
        while (focus < startVcn + count)
        {
            runIdx = _cookedRuns.FindDataRun(focus, runIdx);
            var run = _cookedRuns[runIdx];

            if (run.IsSparse)
            {
                focus += run.Length;
            }
            else
            {
                if (focus != run.StartVcn)
                {
                    _cookedRuns.SplitRun(runIdx, focus);
                    runIdx++;
                    run = _cookedRuns[runIdx];
                }

                var numClusters = Math.Min(startVcn + count - focus, run.Length);
                if (numClusters != run.Length)
                {
                    _cookedRuns.SplitRun(runIdx, focus + numClusters);
                    run = _cookedRuns[runIdx];
                }

                _context.ClusterBitmap.FreeClusters(new Range<long, long>(run.StartLcn, run.Length));
                _cookedRuns.MakeSparse(runIdx);
                totalReleased += (int)run.Length;

                focus += numClusters;
            }
        }

        return totalReleased;
    }

    public async ValueTask<int> ReleaseClustersAsync(long startVcn, int count, CancellationToken cancellationToken)
    {
        var runIdx = 0;

        var totalReleased = 0;

        var focus = startVcn;
        while (focus < startVcn + count)
        {
            runIdx = _cookedRuns.FindDataRun(focus, runIdx);
            var run = _cookedRuns[runIdx];

            if (run.IsSparse)
            {
                focus += run.Length;
            }
            else
            {
                if (focus != run.StartVcn)
                {
                    _cookedRuns.SplitRun(runIdx, focus);
                    runIdx++;
                    run = _cookedRuns[runIdx];
                }

                var numClusters = Math.Min(startVcn + count - focus, run.Length);
                if (numClusters != run.Length)
                {
                    _cookedRuns.SplitRun(runIdx, focus + numClusters);
                    run = _cookedRuns[runIdx];
                }

                await _context.ClusterBitmap.FreeClustersAsync(new Range<long, long>(run.StartLcn, run.Length), cancellationToken).ConfigureAwait(false);
                _cookedRuns.MakeSparse(runIdx);
                totalReleased += (int)run.Length;

                focus += numClusters;
            }
        }

        return totalReleased;
    }

    public override void ReadClusters(long startVcn, int count, byte[] buffer, int offset)
    {
        StreamUtilities.AssertBufferParameters(buffer, offset, count * _bytesPerCluster);

        var runIdx = 0;
        var totalRead = 0;
        while (totalRead < count)
        {
            var focusVcn = startVcn + totalRead;

            runIdx = _cookedRuns.FindDataRun(focusVcn, runIdx);
            var run = _cookedRuns[runIdx];

            var toRead = (int)Math.Min(count - totalRead, run.Length - (focusVcn - run.StartVcn));

            if (run.IsSparse)
            {
                Array.Clear(buffer, offset + totalRead * _bytesPerCluster, toRead * _bytesPerCluster);
            }
            else
            {
                var lcn = _cookedRuns[runIdx].StartLcn + (focusVcn - run.StartVcn);
                _fsStream.Position = lcn * _bytesPerCluster;
                _fsStream.ReadExactly(buffer, offset + totalRead * _bytesPerCluster, toRead * _bytesPerCluster);
            }

            totalRead += toRead;
        }
    }

    public override void ReadClusters(long startVcn, int count, Span<byte> buffer)
    {
        var runIdx = 0;
        var totalRead = 0;
        while (totalRead < count)
        {
            var focusVcn = startVcn + totalRead;

            runIdx = _cookedRuns.FindDataRun(focusVcn, runIdx);
            var run = _cookedRuns[runIdx];

            var toRead = (int)Math.Min(count - totalRead, run.Length - (focusVcn - run.StartVcn));

            if (run.IsSparse)
            {
                buffer.Slice(totalRead * _bytesPerCluster, toRead * _bytesPerCluster).Clear();
            }
            else
            {
                var lcn = _cookedRuns[runIdx].StartLcn + (focusVcn - run.StartVcn);
                _fsStream.Position = lcn * _bytesPerCluster;
                _fsStream.ReadExactly(buffer.Slice(totalRead * _bytesPerCluster, toRead * _bytesPerCluster));
            }

            totalRead += toRead;
        }
    }

    public override async ValueTask ReadClustersAsync(long startVcn, int count, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var runIdx = 0;
        var totalRead = 0;
        while (totalRead < count)
        {
            var focusVcn = startVcn + totalRead;

            runIdx = _cookedRuns.FindDataRun(focusVcn, runIdx);
            var run = _cookedRuns[runIdx];

            var toRead = (int)Math.Min(count - totalRead, run.Length - (focusVcn - run.StartVcn));

            if (run.IsSparse)
            {
                buffer.Slice(totalRead * _bytesPerCluster, toRead * _bytesPerCluster).Span.Clear();
            }
            else
            {
                var lcn = _cookedRuns[runIdx].StartLcn + (focusVcn - run.StartVcn);
                _fsStream.Position = lcn * _bytesPerCluster;
                await _fsStream.ReadExactlyAsync(buffer.Slice(totalRead * _bytesPerCluster, toRead * _bytesPerCluster), cancellationToken).ConfigureAwait(false);
            }

            totalRead += toRead;
        }
    }

    public override int WriteClusters(long startVcn, int count, byte[] buffer, int offset)
    {
        StreamUtilities.AssertBufferParameters(buffer, offset, count * _bytesPerCluster);

        var runIdx = 0;
        var totalWritten = 0;
        while (totalWritten < count)
        {
            var focusVcn = startVcn + totalWritten;

            runIdx = _cookedRuns.FindDataRun(focusVcn, runIdx);
            var run = _cookedRuns[runIdx];

            if (run.IsSparse)
            {
                throw new NotImplementedException("Writing to sparse datarun");
            }

            var toWrite = (int)Math.Min(count - totalWritten, run.Length - (focusVcn - run.StartVcn));

            var lcn = _cookedRuns[runIdx].StartLcn + (focusVcn - run.StartVcn);
            _fsStream.Position = lcn * _bytesPerCluster;
            _fsStream.Write(buffer, offset + totalWritten * _bytesPerCluster, toWrite * _bytesPerCluster);

            totalWritten += toWrite;
        }

        return 0;
    }

    public override int WriteClusters(long startVcn, int count, ReadOnlySpan<byte> buffer)
    {
        var runIdx = 0;
        var totalWritten = 0;
        while (totalWritten < count)
        {
            var focusVcn = startVcn + totalWritten;

            runIdx = _cookedRuns.FindDataRun(focusVcn, runIdx);
            var run = _cookedRuns[runIdx];

            if (run.IsSparse)
            {
                throw new NotImplementedException("Writing to sparse datarun");
            }

            var toWrite = (int)Math.Min(count - totalWritten, run.Length - (focusVcn - run.StartVcn));

            var lcn = _cookedRuns[runIdx].StartLcn + (focusVcn - run.StartVcn);
            _fsStream.Position = lcn * _bytesPerCluster;
            _fsStream.Write(buffer.Slice(totalWritten * _bytesPerCluster, toWrite * _bytesPerCluster));

            totalWritten += toWrite;
        }

        return 0;
    }

    public override async ValueTask<int> WriteClustersAsync(long startVcn, int count, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var runIdx = 0;
        var totalWritten = 0;
        while (totalWritten < count)
        {
            var focusVcn = startVcn + totalWritten;

            runIdx = _cookedRuns.FindDataRun(focusVcn, runIdx);
            var run = _cookedRuns[runIdx];

            if (run.IsSparse)
            {
                throw new NotImplementedException("Writing to sparse datarun");
            }

            var toWrite = (int)Math.Min(count - totalWritten, run.Length - (focusVcn - run.StartVcn));

            var lcn = _cookedRuns[runIdx].StartLcn + (focusVcn - run.StartVcn);
            _fsStream.Position = lcn * _bytesPerCluster;
            await _fsStream.WriteAsync(buffer.Slice(totalWritten * _bytesPerCluster, toWrite * _bytesPerCluster), cancellationToken).ConfigureAwait(false);

            totalWritten += toWrite;
        }

        return 0;
    }

    public override int ClearClusters(long startVcn, int count)
    {
        var zeroBuffer = ArrayPool<byte>.Shared.Rent(16 * _bytesPerCluster);
        try
        {
            zeroBuffer.AsSpan(0, 16 * _bytesPerCluster).Clear();

            var clustersAllocated = 0;

            var numWritten = 0;
            while (numWritten < count)
            {
                var toWrite = Math.Min(count - numWritten, 16);

                clustersAllocated += WriteClusters(startVcn + numWritten, toWrite, zeroBuffer, 0);

                numWritten += toWrite;
            }

            return -clustersAllocated;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(zeroBuffer);
        }
    }

    public override async ValueTask<int> ClearClustersAsync(long startVcn, int count, CancellationToken cancellationToken)
    {
        var zeroBuffer = ArrayPool<byte>.Shared.Rent(16 * _bytesPerCluster);
        try
        {
            zeroBuffer.AsMemory(0, 16 * _bytesPerCluster).Span.Clear();

            var clustersAllocated = 0;

            var numWritten = 0;
            while (numWritten < count)
            {
                var toWrite = Math.Min(count - numWritten, 16);

                clustersAllocated += await WriteClustersAsync(startVcn + numWritten, toWrite, zeroBuffer, cancellationToken).ConfigureAwait(false);

                numWritten += toWrite;
            }

            return -clustersAllocated;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(zeroBuffer);
        }
    }
}