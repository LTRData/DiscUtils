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
using DiscUtils.Compression;
using DiscUtils.Streams;

namespace DiscUtils.Ntfs;

internal sealed class CompressedClusterStream : ClusterStream
{
    private readonly NtfsAttribute _attr;
    private readonly int _bytesPerCluster;

    private readonly byte[] _cacheBuffer;
    private long _cacheBufferVcn = -1;
    private readonly INtfsContext _context;
    private readonly byte[] _ioBuffer;
    private readonly RawClusterStream _rawStream;

    public CompressedClusterStream(INtfsContext context, NtfsAttribute attr, RawClusterStream rawStream)
    {
        _context = context;
        _attr = attr;
        _rawStream = rawStream;
        _bytesPerCluster = _context.BiosParameterBlock.BytesPerCluster;

        _cacheBuffer = new byte[_attr.CompressionUnitSize * context.BiosParameterBlock.BytesPerCluster];
        _ioBuffer = new byte[_attr.CompressionUnitSize * context.BiosParameterBlock.BytesPerCluster];
    }

    public override long AllocatedClusterCount => _rawStream.AllocatedClusterCount;

    public override IEnumerable<Range<long, long>> StoredClusters => Range<long, long>.Chunked(_rawStream.StoredClusters, _attr.CompressionUnitSize);

    public override bool IsClusterStored(long vcn)
    {
        return _rawStream.IsClusterStored(CompressionStart(vcn));
    }

    public override void ExpandToClusters(long numVirtualClusters, NonResidentAttributeRecord extent, bool allocate)
    {
        _rawStream.ExpandToClusters(MathUtilities.RoundUp(numVirtualClusters, _attr.CompressionUnitSize), extent, false);
    }

    public override ValueTask ExpandToClustersAsync(long numVirtualClusters, NonResidentAttributeRecord extent, bool allocate, CancellationToken cancellationToken)
    {
        return _rawStream.ExpandToClustersAsync(MathUtilities.RoundUp(numVirtualClusters, _attr.CompressionUnitSize), extent, false, cancellationToken);
    }

    public override void TruncateToClusters(long numVirtualClusters)
    {
        var alignedNum = MathUtilities.RoundUp(numVirtualClusters, _attr.CompressionUnitSize);
        _rawStream.TruncateToClusters(alignedNum);
        if (alignedNum != numVirtualClusters)
        {
            _rawStream.ReleaseClusters(numVirtualClusters, (int)(alignedNum - numVirtualClusters));
        }
    }

    public override void ReadClusters(long startVcn, int count, byte[] buffer, int offset)
    {
        if (buffer.Length < count * _bytesPerCluster + offset)
        {
            throw new ArgumentException("Cluster buffer too small", nameof(buffer));
        }

        var totalRead = 0;
        while (totalRead < count)
        {
            var focusVcn = startVcn + totalRead;
            LoadCache(focusVcn);

            var cacheOffset = (int)(focusVcn - _cacheBufferVcn);
            var toCopy = Math.Min(_attr.CompressionUnitSize - cacheOffset, count - totalRead);

            System.Buffer.BlockCopy(_cacheBuffer, cacheOffset * _bytesPerCluster, buffer, offset + totalRead * _bytesPerCluster,
                toCopy * _bytesPerCluster);

            totalRead += toCopy;
        }
    }

    public override void ReadClusters(long startVcn, int count, Span<byte> buffer)
    {
        if (buffer.Length < count * _bytesPerCluster)
        {
            throw new ArgumentException("Cluster buffer too small", nameof(buffer));
        }

        var totalRead = 0;
        while (totalRead < count)
        {
            var focusVcn = startVcn + totalRead;
            LoadCache(focusVcn);

            var cacheOffset = (int)(focusVcn - _cacheBufferVcn);
            var toCopy = Math.Min(_attr.CompressionUnitSize - cacheOffset, count - totalRead);

            _cacheBuffer.AsSpan(cacheOffset * _bytesPerCluster, toCopy * _bytesPerCluster).CopyTo(buffer.Slice(totalRead * _bytesPerCluster));

            totalRead += toCopy;
        }
    }

    public override async ValueTask ReadClustersAsync(long startVcn, int count, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length < count * _bytesPerCluster)
        {
            throw new ArgumentException("Cluster buffer too small", nameof(buffer));
        }

        var totalRead = 0;
        while (totalRead < count)
        {
            var focusVcn = startVcn + totalRead;
            await LoadCacheAsync(focusVcn, cancellationToken).ConfigureAwait(false);

            var cacheOffset = (int)(focusVcn - _cacheBufferVcn);
            var toCopy = Math.Min(_attr.CompressionUnitSize - cacheOffset, count - totalRead);

            _cacheBuffer.AsMemory(cacheOffset * _bytesPerCluster, toCopy * _bytesPerCluster).CopyTo(buffer.Slice(totalRead * _bytesPerCluster));

            totalRead += toCopy;
        }
    }

    public override int WriteClusters(long startVcn, int count, byte[] buffer, int offset)
    {
        if (buffer.Length < count * _bytesPerCluster + offset)
        {
            throw new ArgumentException("Cluster buffer too small", nameof(buffer));
        }

        var totalAllocated = 0;

        var totalWritten = 0;
        while (totalWritten < count)
        {
            var focusVcn = startVcn + totalWritten;
            var cuStart = CompressionStart(focusVcn);

            if (cuStart == focusVcn && count - totalWritten >= _attr.CompressionUnitSize)
            {
                // Aligned write...
                totalAllocated += CompressAndWriteClusters(focusVcn, _attr.CompressionUnitSize,
                    buffer.AsSpan(offset + totalWritten * _bytesPerCluster));

                totalWritten += _attr.CompressionUnitSize;
            }
            else
            {
                // Unaligned, so go through cache
                LoadCache(focusVcn);

                var cacheOffset = (int)(focusVcn - _cacheBufferVcn);
                var toCopy = Math.Min(count - totalWritten, _attr.CompressionUnitSize - cacheOffset);

                System.Buffer.BlockCopy(buffer, offset + totalWritten * _bytesPerCluster, _cacheBuffer,
                    cacheOffset * _bytesPerCluster, toCopy * _bytesPerCluster);

                totalAllocated += CompressAndWriteClusters(_cacheBufferVcn, _attr.CompressionUnitSize, _cacheBuffer);

                totalWritten += toCopy;
            }
        }

        return totalAllocated;
    }

    public override int WriteClusters(long startVcn, int count, ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < count * _bytesPerCluster)
        {
            throw new ArgumentException("Cluster buffer too small", nameof(buffer));
        }

        var totalAllocated = 0;

        var totalWritten = 0;
        while (totalWritten < count)
        {
            var focusVcn = startVcn + totalWritten;
            var cuStart = CompressionStart(focusVcn);

            if (cuStart == focusVcn && count - totalWritten >= _attr.CompressionUnitSize)
            {
                // Aligned write...
                var bytes = buffer.Slice(totalWritten * _bytesPerCluster);
                totalAllocated += CompressAndWriteClusters(focusVcn, _attr.CompressionUnitSize, bytes);

                totalWritten += _attr.CompressionUnitSize;
            }
            else
            {
                // Unaligned, so go through cache
                LoadCache(focusVcn);

                var cacheOffset = (int)(focusVcn - _cacheBufferVcn);
                var toCopy = Math.Min(count - totalWritten, _attr.CompressionUnitSize - cacheOffset);

                buffer.Slice(totalWritten * _bytesPerCluster, toCopy * _bytesPerCluster).CopyTo(_cacheBuffer.AsSpan(cacheOffset * _bytesPerCluster));

                totalAllocated += CompressAndWriteClusters(_cacheBufferVcn, _attr.CompressionUnitSize, _cacheBuffer);

                totalWritten += toCopy;
            }
        }

        return totalAllocated;
    }

    public override async ValueTask<int> WriteClustersAsync(long startVcn, int count, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length < count * _bytesPerCluster)
        {
            throw new ArgumentException("Cluster buffer too small", nameof(buffer));
        }

        var totalAllocated = 0;

        var totalWritten = 0;
        while (totalWritten < count)
        {
            var focusVcn = startVcn + totalWritten;
            var cuStart = CompressionStart(focusVcn);

            if (cuStart == focusVcn && count - totalWritten >= _attr.CompressionUnitSize)
            {
                // Aligned write...
                var bytes = buffer.Slice(totalWritten * _bytesPerCluster);
                totalAllocated += await CompressAndWriteClustersAsync(focusVcn, _attr.CompressionUnitSize, bytes, cancellationToken).ConfigureAwait(false);

                totalWritten += _attr.CompressionUnitSize;
            }
            else
            {
                // Unaligned, so go through cache
                await LoadCacheAsync(focusVcn, cancellationToken).ConfigureAwait(false);

                var cacheOffset = (int)(focusVcn - _cacheBufferVcn);
                var toCopy = Math.Min(count - totalWritten, _attr.CompressionUnitSize - cacheOffset);

                buffer.Slice(totalWritten * _bytesPerCluster, toCopy * _bytesPerCluster).CopyTo(_cacheBuffer.AsMemory(cacheOffset * _bytesPerCluster));

                totalAllocated += await CompressAndWriteClustersAsync(_cacheBufferVcn, _attr.CompressionUnitSize, _cacheBuffer, cancellationToken).ConfigureAwait(false);

                totalWritten += toCopy;
            }
        }

        return totalAllocated;
    }

    public override int ClearClusters(long startVcn, int count)
    {
        var totalReleased = 0;
        var totalCleared = 0;
        while (totalCleared < count)
        {
            var focusVcn = startVcn + totalCleared;
            if (CompressionStart(focusVcn) == focusVcn && count - totalCleared >= _attr.CompressionUnitSize)
            {
                // Aligned - so it's a sparse compression unit...
                totalReleased += _rawStream.ReleaseClusters(startVcn, _attr.CompressionUnitSize);
                totalCleared += _attr.CompressionUnitSize;
            }
            else
            {
                var toZero =
                    (int)
                    Math.Min(count - totalCleared,
                        _attr.CompressionUnitSize - (focusVcn - CompressionStart(focusVcn)));
                totalReleased -= WriteZeroClusters(focusVcn, toZero);
                totalCleared += toZero;
            }
        }

        return totalReleased;
    }

    public override async ValueTask<int> ClearClustersAsync(long startVcn, int count, CancellationToken cancellationToken)
    {
        var totalReleased = 0;
        var totalCleared = 0;
        while (totalCleared < count)
        {
            var focusVcn = startVcn + totalCleared;
            if (CompressionStart(focusVcn) == focusVcn && count - totalCleared >= _attr.CompressionUnitSize)
            {
                // Aligned - so it's a sparse compression unit...
                totalReleased += await _rawStream.ReleaseClustersAsync(startVcn, _attr.CompressionUnitSize, cancellationToken).ConfigureAwait(false);
                totalCleared += _attr.CompressionUnitSize;
            }
            else
            {
                var toZero =
                    (int)
                    Math.Min(count - totalCleared,
                        _attr.CompressionUnitSize - (focusVcn - CompressionStart(focusVcn)));
                totalReleased -= await WriteZeroClustersAsync(focusVcn, toZero, cancellationToken).ConfigureAwait(false);
                totalCleared += toZero;
            }
        }

        return totalReleased;
    }

    private int WriteZeroClusters(long focusVcn, int count)
    {
        var allocatedClusters = 0;

        var zeroBuffer = ArrayPool<byte>.Shared.Rent(16 * _bytesPerCluster);
        try
        {
            Array.Clear(zeroBuffer, 0, 16 * _bytesPerCluster);

            var numWritten = 0;
            while (numWritten < count)
            {
                var toWrite = Math.Min(count - numWritten, 16);

                allocatedClusters += WriteClusters(focusVcn + numWritten, toWrite, zeroBuffer, 0);

                numWritten += toWrite;
            }

            return allocatedClusters;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(zeroBuffer);
        }
    }

    private async ValueTask<int> WriteZeroClustersAsync(long focusVcn, int count, CancellationToken cancellationToken)
    {
        var allocatedClusters = 0;

        var bufferSize = 16 * _bytesPerCluster;
        var zeroBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            Array.Clear(zeroBuffer, 0, bufferSize);

            var numWritten = 0;
            while (numWritten < count)
            {
                var toWrite = Math.Min(count - numWritten, 16);

                allocatedClusters += await WriteClustersAsync(focusVcn + numWritten, toWrite, zeroBuffer.AsMemory(0, bufferSize), cancellationToken).ConfigureAwait(false);

                numWritten += toWrite;
            }

            return allocatedClusters;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(zeroBuffer);
        }
    }

    private int CompressAndWriteClusters(long focusVcn, int count, ReadOnlySpan<byte> buffer)
    {
        var compressor = _context.Options.Compressor;
        compressor.BlockSize = _bytesPerCluster;

        var totalAllocated = 0;

        var result = compressor.Compress(buffer.Slice(_attr.CompressionUnitSize * _bytesPerCluster), _ioBuffer,
            out var compressedLength);
        if (result == CompressionResult.AllZeros)
        {
            totalAllocated -= _rawStream.ReleaseClusters(focusVcn, count);
        }
        else if (result == CompressionResult.Compressed &&
                 _attr.CompressionUnitSize * _bytesPerCluster - compressedLength > _bytesPerCluster)
        {
            var compClusters = MathUtilities.Ceil(compressedLength, _bytesPerCluster);
            totalAllocated += _rawStream.AllocateClusters(focusVcn, compClusters);
            totalAllocated += _rawStream.WriteClusters(focusVcn, compClusters, _ioBuffer, 0);
            totalAllocated -= _rawStream.ReleaseClusters(focusVcn + compClusters,
                _attr.CompressionUnitSize - compClusters);
        }
        else
        {
            totalAllocated += _rawStream.AllocateClusters(focusVcn, _attr.CompressionUnitSize);
            totalAllocated += _rawStream.WriteClusters(focusVcn, _attr.CompressionUnitSize, buffer);
        }

        return totalAllocated;
    }

    private async ValueTask<int> CompressAndWriteClustersAsync(long focusVcn, int count, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var compressor = _context.Options.Compressor;
        compressor.BlockSize = _bytesPerCluster;

        var totalAllocated = 0;

        var result = compressor.Compress(buffer.Span.Slice(_attr.CompressionUnitSize * _bytesPerCluster), _ioBuffer,
            out var compressedLength);
        if (result == CompressionResult.AllZeros)
        {
            totalAllocated -= await _rawStream.ReleaseClustersAsync(focusVcn, count, cancellationToken).ConfigureAwait(false);
        }
        else if (result == CompressionResult.Compressed &&
                 _attr.CompressionUnitSize * _bytesPerCluster - compressedLength > _bytesPerCluster)
        {
            var compClusters = MathUtilities.Ceil(compressedLength, _bytesPerCluster);
            totalAllocated += await _rawStream.AllocateClustersAsync(focusVcn, compClusters, cancellationToken).ConfigureAwait(false);
            totalAllocated += await _rawStream.WriteClustersAsync(focusVcn, compClusters, _ioBuffer, cancellationToken).ConfigureAwait(false);
            totalAllocated -= await _rawStream.ReleaseClustersAsync(focusVcn + compClusters,
                _attr.CompressionUnitSize - compClusters, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            totalAllocated += await _rawStream.AllocateClustersAsync(focusVcn, _attr.CompressionUnitSize, cancellationToken).ConfigureAwait(false);
            totalAllocated += await _rawStream.WriteClustersAsync(focusVcn, _attr.CompressionUnitSize, buffer, cancellationToken).ConfigureAwait(false);
        }

        return totalAllocated;
    }

    private long CompressionStart(long vcn)
    {
        return MathUtilities.RoundDown(vcn, _attr.CompressionUnitSize);
    }

    private void LoadCache(long vcn)
    {
        var cuStart = CompressionStart(vcn);
        if (_cacheBufferVcn != cuStart)
        {
            if (_rawStream.AreAllClustersStored(cuStart, _attr.CompressionUnitSize))
            {
                // Uncompressed data - read straight into cache buffer
                _rawStream.ReadClusters(cuStart, _attr.CompressionUnitSize, _cacheBuffer, 0);
            }
            else if (_rawStream.IsClusterStored(cuStart))
            {
                // Compressed data - read via IO buffer
                _rawStream.ReadClusters(cuStart, _attr.CompressionUnitSize, _ioBuffer, 0);

                var expected =
                    (int)
                    Math.Min(_attr.Length - vcn * _bytesPerCluster, _attr.CompressionUnitSize * _bytesPerCluster);

                var decomp = _context.Options.Compressor.Decompress(_ioBuffer, _cacheBuffer);
                if (decomp < expected)
                {
                    throw new IOException("Decompression returned too little data");
                }
            }
            else
            {
                // Sparse, wipe cache buffer directly
                Array.Clear(_cacheBuffer, 0, _cacheBuffer.Length);
            }

            _cacheBufferVcn = cuStart;
        }
    }

    private async ValueTask LoadCacheAsync(long vcn, CancellationToken cancellationToken)
    {
        var cuStart = CompressionStart(vcn);
        if (_cacheBufferVcn != cuStart)
        {
            if (_rawStream.AreAllClustersStored(cuStart, _attr.CompressionUnitSize))
            {
                // Uncompressed data - read straight into cache buffer
                await _rawStream.ReadClustersAsync(cuStart, _attr.CompressionUnitSize, _cacheBuffer, cancellationToken).ConfigureAwait(false);
            }
            else if (_rawStream.IsClusterStored(cuStart))
            {
                // Compressed data - read via IO buffer
                await _rawStream.ReadClustersAsync(cuStart, _attr.CompressionUnitSize, _ioBuffer, cancellationToken).ConfigureAwait(false);

                var expected =
                    (int)
                    Math.Min(_attr.Length - vcn * _bytesPerCluster, _attr.CompressionUnitSize * _bytesPerCluster);

                var decomp = _context.Options.Compressor.Decompress(_ioBuffer, _cacheBuffer);
                if (decomp < expected)
                {
                    throw new IOException("Decompression returned too little data");
                }
            }
            else
            {
                // Sparse, wipe cache buffer directly
                Array.Clear(_cacheBuffer, 0, _cacheBuffer.Length);
            }

            _cacheBufferVcn = cuStart;
        }
    }
}