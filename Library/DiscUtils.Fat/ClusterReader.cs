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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Fat;

internal sealed class ClusterReader
{
    private readonly int _bytesPerSector;

    /// <summary>
    /// Pre-calculated value because of number of uses of this externally.
    /// </summary>
    private readonly int _clusterSize;

    private readonly int _firstDataSector;
    private readonly int _sectorsPerCluster;
    private readonly Stream _stream;

    public ClusterReader(Stream stream, int firstDataSector, int sectorsPerCluster, int bytesPerSector)
    {
        _stream = stream;
        _firstDataSector = firstDataSector;
        _sectorsPerCluster = sectorsPerCluster;
        _bytesPerSector = bytesPerSector;

        _clusterSize = _sectorsPerCluster * _bytesPerSector;
    }

    public int ClusterSize => _clusterSize;

    public int BytesPerSector => _bytesPerSector;

    public int SectorsPerCluster => _sectorsPerCluster;

    public void ReadCluster(uint cluster, byte[] buffer, int offset)
    {
        if (offset + ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * _sectorsPerCluster + _firstDataSector);

        _stream.Position = firstSector * _bytesPerSector;
        _stream.ReadExactly(buffer, offset, _clusterSize);
    }

    public void ReadCluster(uint cluster, Span<byte> buffer)
    {
        if (ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * _sectorsPerCluster + _firstDataSector);

        _stream.Position = firstSector * _bytesPerSector;
        _stream.ReadExactly(buffer.Slice(0, _clusterSize));
    }

    public ValueTask ReadClusterAsync(uint cluster, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * _sectorsPerCluster + _firstDataSector);

        _stream.Position = firstSector * _bytesPerSector;
        return _stream.ReadExactlyAsync(buffer.Slice(0, _clusterSize), cancellationToken);
    }

    internal void WriteCluster(uint cluster, byte[] buffer, int offset)
    {
        if (offset + ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * _sectorsPerCluster + _firstDataSector);

        _stream.Position = firstSector * _bytesPerSector;

        _stream.Write(buffer, offset, _clusterSize);
    }

    internal void WriteCluster(uint cluster, ReadOnlySpan<byte> buffer)
    {
        if (ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * _sectorsPerCluster + _firstDataSector);

        _stream.Position = firstSector * _bytesPerSector;

        _stream.Write(buffer.Slice(0, _clusterSize));
    }

    internal ValueTask WriteClusterAsync(uint cluster, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * _sectorsPerCluster + _firstDataSector);

        _stream.Position = firstSector * _bytesPerSector;

        return _stream.WriteAsync(buffer.Slice(0, _clusterSize), cancellationToken);
    }

    internal void WipeCluster(uint cluster)
    {
        var firstSector = (uint)((cluster - 2) * _sectorsPerCluster + _firstDataSector);

        _stream.Position = firstSector * _bytesPerSector;

        var buffer = ArrayPool<byte>.Shared.Rent(_clusterSize);
        try
        {
            Array.Clear(buffer, 0, _clusterSize);
            _stream.Write(buffer, 0, _clusterSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal async ValueTask WipeClusterAsync(uint cluster, CancellationToken cancellationToken)
    {
        var firstSector = (uint)((cluster - 2) * _sectorsPerCluster + _firstDataSector);

        _stream.Position = firstSector * _bytesPerSector;

        var buffer = ArrayPool<byte>.Shared.Rent(_clusterSize);
        try
        {
            Array.Clear(buffer, 0, _clusterSize);
            await _stream.WriteAsync(buffer.AsMemory(0, _clusterSize), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}