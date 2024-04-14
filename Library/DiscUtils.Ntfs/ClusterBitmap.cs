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

internal class ClusterBitmap : IDisposable
{
    private Bitmap _bitmap;
    private readonly File _file;
    private bool _fragmentedDiskMode;

    private long _nextDataCluster;

    public ClusterBitmap(File file)
    {
        _file = file;
        _bitmap = new Bitmap(
            _file.OpenStream(AttributeType.Data, null, FileAccess.ReadWrite),
            MathUtilities.Ceil(file.Context.BiosParameterBlock.TotalSectors64,
                file.Context.BiosParameterBlock.SectorsPerCluster));
    }

    internal Bitmap Bitmap => _bitmap;

    public void Dispose()
    {
        if (_bitmap != null)
        {
            _bitmap.Dispose();
            _bitmap = null;
        }
    }

    /// <summary>
    /// Allocates clusters from the disk.
    /// </summary>
    /// <param name="count">The number of clusters to allocate.</param>
    /// <param name="proposedStart">The proposed start cluster (or -1).</param>
    /// <param name="isMft"><c>true</c> if this attribute is the $MFT\$DATA attribute.</param>
    /// <param name="total">The total number of clusters in the file, including this allocation.</param>
    /// <returns>The list of cluster allocations.</returns>
    public List<Range<long, long>> AllocateClusters(long count, long proposedStart, bool isMft, long total)
    {
        var result = new List<Range<long, long>>();

        long numFound = 0;

        var totalClusters = _file.Context.RawStream.Length / _file.Context.BiosParameterBlock.BytesPerCluster;

        if (isMft)
        {
            // First, try to extend the existing cluster run (if available)
            if (proposedStart >= 0)
            {
                numFound += ExtendRun(count - numFound, result, proposedStart, totalClusters);
            }

            // The MFT grows sequentially across the disk
            if (numFound < count && !_fragmentedDiskMode)
            {
                numFound += FindClusters(count - numFound, result, 0, totalClusters, isMft, true, 0);
            }

            if (numFound < count)
            {
                numFound += FindClusters(count - numFound, result, 0, totalClusters, isMft, false, 0);
            }
        }
        else
        {
            // First, try to extend the existing cluster run (if available)
            if (proposedStart >= 0)
            {
                numFound += ExtendRun(count - numFound, result, proposedStart, totalClusters);
            }

            // Try to find a contiguous range
            if (numFound < count && !_fragmentedDiskMode)
            {
                numFound += FindClusters(count - numFound, result, totalClusters / 8, totalClusters, isMft, true,
                    total / 4);
            }

            if (numFound < count)
            {
                numFound += FindClusters(count - numFound, result, totalClusters / 8, totalClusters, isMft, false, 0);
            }

            if (numFound < count)
            {
                numFound = FindClusters(count - numFound, result, totalClusters / 16, totalClusters / 8, isMft, false, 0);
            }

            if (numFound < count)
            {
                numFound = FindClusters(count - numFound, result, totalClusters / 32, totalClusters / 16, isMft, false,
                    0);
            }

            if (numFound < count)
            {
                numFound = FindClusters(count - numFound, result, 0, totalClusters / 32, isMft, false, 0);
            }
        }

        if (numFound < count)
        {
            FreeClusters(result);
            throw new IOException("Out of disk space");
        }

        // If we found more than two clusters, or we have a fragmented result,
        // then switch out of trying to allocate contiguous ranges.  Similarly,
        // switch back if we found a reasonable quantity in a single span.
        if ((numFound > 4 && result.Count == 1) || result.Count > 1)
        {
            _fragmentedDiskMode = numFound / result.Count < 4;
        }

        return result;
    }

    /// <summary>
    /// Allocates clusters from the disk.
    /// </summary>
    /// <param name="count">The number of clusters to allocate.</param>
    /// <param name="proposedStart">The proposed start cluster (or -1).</param>
    /// <param name="isMft"><c>true</c> if this attribute is the $MFT\$DATA attribute.</param>
    /// <param name="total">The total number of clusters in the file, including this allocation.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The list of cluster allocations.</returns>
    public async ValueTask<List<Range<long, long>>> AllocateClustersAsync(long count, long proposedStart, bool isMft, long total, CancellationToken cancellationToken)
    {
        var result = new List<Range<long, long>>();

        long numFound = 0;

        var totalClusters = _file.Context.RawStream.Length / _file.Context.BiosParameterBlock.BytesPerCluster;

        if (isMft)
        {
            // First, try to extend the existing cluster run (if available)
            if (proposedStart >= 0)
            {
                numFound += await ExtendRunAsync(count - numFound, result, proposedStart, totalClusters, cancellationToken).ConfigureAwait(false);
            }

            // The MFT grows sequentially across the disk
            if (numFound < count && !_fragmentedDiskMode)
            {
                numFound += await FindClustersAsync(count - numFound, result, 0, totalClusters, isMft, true, 0, cancellationToken).ConfigureAwait(false);
            }

            if (numFound < count)
            {
                numFound += await FindClustersAsync(count - numFound, result, 0, totalClusters, isMft, false, 0, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            // First, try to extend the existing cluster run (if available)
            if (proposedStart >= 0)
            {
                numFound += await ExtendRunAsync(count - numFound, result, proposedStart, totalClusters, cancellationToken).ConfigureAwait(false);
            }

            // Try to find a contiguous range
            if (numFound < count && !_fragmentedDiskMode)
            {
                numFound += await FindClustersAsync(count - numFound, result, totalClusters / 8, totalClusters, isMft, true,
                    total / 4, cancellationToken).ConfigureAwait(false);
            }

            if (numFound < count)
            {
                numFound += await FindClustersAsync(count - numFound, result, totalClusters / 8, totalClusters, isMft, false, 0, cancellationToken).ConfigureAwait(false);
            }

            if (numFound < count)
            {
                numFound = await FindClustersAsync(count - numFound, result, totalClusters / 16, totalClusters / 8, isMft, false, 0, cancellationToken).ConfigureAwait(false);
            }

            if (numFound < count)
            {
                numFound = await FindClustersAsync(count - numFound, result, totalClusters / 32, totalClusters / 16, isMft, false,
                    0, cancellationToken).ConfigureAwait(false);
            }

            if (numFound < count)
            {
                numFound = await FindClustersAsync(count - numFound, result, 0, totalClusters / 32, isMft, false, 0, cancellationToken).ConfigureAwait(false);
            }
        }

        if (numFound < count)
        {
            await FreeClustersAsync(result, cancellationToken).ConfigureAwait(false);
            throw new IOException("Out of disk space");
        }

        // If we found more than two clusters, or we have a fragmented result,
        // then switch out of trying to allocate contiguous ranges.  Similarly,
        // switch back if we found a reasonable quantity in a single span.
        if ((numFound > 4 && result.Count == 1) || result.Count > 1)
        {
            _fragmentedDiskMode = numFound / result.Count < 4;
        }

        return result;
    }

    internal void MarkAllocated(long first, long count)
    {
        _bitmap.MarkPresentRange(first, count);
    }

    internal void FreeClusters(IEnumerable<Range<long, long>> runs)
    {
        foreach (var run in runs)
        {
            _bitmap.MarkAbsentRange(run.Offset, run.Count);
        }
    }

    internal async ValueTask FreeClustersAsync(IEnumerable<Range<long, long>> runs, CancellationToken cancellationToken)
    {
        foreach (var run in runs)
        {
            await _bitmap.MarkAbsentRangeAsync(run.Offset, run.Count, cancellationToken).ConfigureAwait(false);
        }
    }

    internal void FreeClusters(Range<long, long> run)
    {
        _bitmap.MarkAbsentRange(run.Offset, run.Count);
    }

    internal ValueTask FreeClustersAsync(Range<long, long> run, CancellationToken cancellationToken)
    {
        return _bitmap.MarkAbsentRangeAsync(run.Offset, run.Count, cancellationToken);
    }

    /// <summary>
    /// Sets the total number of clusters managed in the volume.
    /// </summary>
    /// <param name="numClusters">Total number of clusters in the volume.</param>
    /// <remarks>
    /// Any clusters represented in the bitmap beyond the total number in the volume are marked as in-use.
    /// </remarks>
    internal void SetTotalClusters(long numClusters)
    {
        var actualClusters = _bitmap.SetTotalEntries(numClusters);
        if (actualClusters != numClusters)
        {
            MarkAllocated(numClusters, actualClusters - numClusters);
        }
    }

    private long ExtendRun(long count, List<Range<long, long>> result, long start, long end)
    {
        var focusCluster = start;
        while (!_bitmap.IsPresent(focusCluster) && focusCluster < end && focusCluster - start < count)
        {
            ++focusCluster;
        }

        var numFound = focusCluster - start;

        if (numFound > 0)
        {
            _bitmap.MarkPresentRange(start, numFound);
            result.Add(new Range<long, long>(start, numFound));
        }

        return numFound;
    }

    private async ValueTask<long> ExtendRunAsync(long count, List<Range<long, long>> result, long start, long end, CancellationToken cancellationToken)
    {
        var focusCluster = start;
        while (!_bitmap.IsPresent(focusCluster) && focusCluster < end && focusCluster - start < count)
        {
            ++focusCluster;
        }

        var numFound = focusCluster - start;

        if (numFound > 0)
        {
            await _bitmap.MarkPresentRangeAsync(start, numFound, cancellationToken).ConfigureAwait(false);
            result.Add(new Range<long, long>(start, numFound));
        }

        return numFound;
    }

    /// <summary>
    /// Finds one or more free clusters in a range.
    /// </summary>
    /// <param name="count">The number of clusters required.</param>
    /// <param name="result">The list of clusters found (i.e. out param).</param>
    /// <param name="start">The first cluster in the range to look at.</param>
    /// <param name="end">The last cluster in the range to look at (exclusive).</param>
    /// <param name="isMft">Indicates if the clusters are for the MFT.</param>
    /// <param name="contiguous">Indicates if contiguous clusters are required.</param>
    /// <param name="headroom">Indicates how many clusters to skip before next allocation, to prevent fragmentation.</param>
    /// <returns>The number of clusters found in the range.</returns>
    private long FindClusters(long count, List<Range<long, long>> result, long start, long end, bool isMft,
                              bool contiguous, long headroom)
    {
        long numFound = 0;

        long focusCluster;
        if (isMft)
        {
            focusCluster = start;
        }
        else
        {
            if (_nextDataCluster < start || _nextDataCluster >= end)
            {
                _nextDataCluster = start;
            }

            focusCluster = _nextDataCluster;
        }

        long numInspected = 0;
        while (numFound < count && focusCluster >= start && numInspected < end - start)
        {
            if (!_bitmap.IsPresent(focusCluster))
            {
                // Start of a run...
                var runStart = focusCluster;
                ++focusCluster;

                while (!_bitmap.IsPresent(focusCluster) && focusCluster - runStart < count - numFound)
                {
                    ++focusCluster;
                    ++numInspected;
                }

                if (!contiguous || focusCluster - runStart == count - numFound)
                {
                    _bitmap.MarkPresentRange(runStart, focusCluster - runStart);

                    result.Add(new Range<long, long>(runStart, focusCluster - runStart));
                    numFound += focusCluster - runStart;
                }
            }
            else
            {
                ++focusCluster;
            }

            ++numInspected;

            if (focusCluster >= end)
            {
                focusCluster = start;
            }
        }

        if (!isMft)
        {
            _nextDataCluster = focusCluster + headroom;
        }

        return numFound;
    }

    /// <summary>
    /// Finds one or more free clusters in a range.
    /// </summary>
    /// <param name="count">The number of clusters required.</param>
    /// <param name="result">The list of clusters found (i.e. out param).</param>
    /// <param name="start">The first cluster in the range to look at.</param>
    /// <param name="end">The last cluster in the range to look at (exclusive).</param>
    /// <param name="isMft">Indicates if the clusters are for the MFT.</param>
    /// <param name="contiguous">Indicates if contiguous clusters are required.</param>
    /// <param name="headroom">Indicates how many clusters to skip before next allocation, to prevent fragmentation.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The number of clusters found in the range.</returns>
    private async ValueTask<long> FindClustersAsync(long count, List<Range<long, long>> result, long start, long end, bool isMft,
                              bool contiguous, long headroom, CancellationToken cancellationToken)
    {
        long numFound = 0;

        long focusCluster;
        if (isMft)
        {
            focusCluster = start;
        }
        else
        {
            if (_nextDataCluster < start || _nextDataCluster >= end)
            {
                _nextDataCluster = start;
            }

            focusCluster = _nextDataCluster;
        }

        long numInspected = 0;
        while (numFound < count && focusCluster >= start && numInspected < end - start)
        {
            if (!_bitmap.IsPresent(focusCluster))
            {
                // Start of a run...
                var runStart = focusCluster;
                ++focusCluster;

                while (!_bitmap.IsPresent(focusCluster) && focusCluster - runStart < count - numFound)
                {
                    ++focusCluster;
                    ++numInspected;
                }

                if (!contiguous || focusCluster - runStart == count - numFound)
                {
                    await _bitmap.MarkPresentRangeAsync(runStart, focusCluster - runStart, cancellationToken).ConfigureAwait(false);

                    result.Add(new Range<long, long>(runStart, focusCluster - runStart));
                    numFound += focusCluster - runStart;
                }
            }
            else
            {
                ++focusCluster;
            }

            ++numInspected;

            if (focusCluster >= end)
            {
                focusCluster = start;
            }
        }

        if (!isMft)
        {
            _nextDataCluster = focusCluster + headroom;
        }

        return numFound;
    }
}
