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
    private readonly int _firstDataSector;

    public ClusterReader(Stream stream, int firstDataSector, int sectorsPerCluster, int bytesPerSector)
    {
        BaseStream = stream;
        _firstDataSector = firstDataSector;
        SectorsPerCluster = sectorsPerCluster;
        BytesPerSector = bytesPerSector;

        ClusterSize = SectorsPerCluster * BytesPerSector;
    }

    public Stream BaseStream { get; }

    public int ClusterSize { get; }

    public int BytesPerSector { get; }

    public int SectorsPerCluster { get; }

    public long GetBaseStreamPositionForCluster(uint cluster)
        => ((uint)((cluster - 2) * SectorsPerCluster + _firstDataSector)) * BytesPerSector;

    public void ReadCluster(uint cluster, byte[] buffer, int offset)
    {
        if (offset + ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * SectorsPerCluster + _firstDataSector);

        BaseStream.Position = firstSector * BytesPerSector;
        BaseStream.ReadExactly(buffer, offset, ClusterSize);
    }

    public void ReadCluster(uint cluster, Span<byte> buffer)
    {
        if (ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * SectorsPerCluster + _firstDataSector);

        BaseStream.Position = firstSector * BytesPerSector;
        BaseStream.ReadExactly(buffer[..ClusterSize]);
    }

    public ValueTask ReadClusterAsync(uint cluster, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * SectorsPerCluster + _firstDataSector);

        BaseStream.Position = firstSector * BytesPerSector;
        return BaseStream.ReadExactlyAsync(buffer[..ClusterSize], cancellationToken);
    }

    internal void WriteCluster(uint cluster, byte[] buffer, int offset)
    {
        if (offset + ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * SectorsPerCluster + _firstDataSector);

        BaseStream.Position = firstSector * BytesPerSector;

        BaseStream.Write(buffer, offset, ClusterSize);
    }

    internal void WriteCluster(uint cluster, ReadOnlySpan<byte> buffer)
    {
        if (ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * SectorsPerCluster + _firstDataSector);

        BaseStream.Position = firstSector * BytesPerSector;

        BaseStream.Write(buffer[..ClusterSize]);
    }

    internal ValueTask WriteClusterAsync(uint cluster, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (ClusterSize > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer),
                "buffer is too small - cluster would overflow buffer");
        }

        var firstSector = (uint)((cluster - 2) * SectorsPerCluster + _firstDataSector);

        BaseStream.Position = firstSector * BytesPerSector;

        return BaseStream.WriteAsync(buffer[..ClusterSize], cancellationToken);
    }

    internal void WipeCluster(uint cluster)
    {
        var firstSector = (uint)((cluster - 2) * SectorsPerCluster + _firstDataSector);

        BaseStream.Position = firstSector * BytesPerSector;

        var buffer = ArrayPool<byte>.Shared.Rent(ClusterSize);
        try
        {
            Array.Clear(buffer, 0, ClusterSize);
            BaseStream.Write(buffer, 0, ClusterSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal async ValueTask WipeClusterAsync(uint cluster, CancellationToken cancellationToken)
    {
        var firstSector = (uint)((cluster - 2) * SectorsPerCluster + _firstDataSector);

        BaseStream.Position = firstSector * BytesPerSector;

        var buffer = ArrayPool<byte>.Shared.Rent(ClusterSize);
        try
        {
            Array.Clear(buffer, 0, ClusterSize);
            await BaseStream.WriteAsync(buffer.AsMemory(0, ClusterSize), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}