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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;

namespace DiscUtils.OpticalDisk;

/// <summary>
/// Interprets a Mode 2 image.
/// </summary>
/// <remarks>
/// Effectively just strips the additional header / footer from the Mode 2 sector
/// data - does not attempt to validate the information.
/// </remarks>
internal class Mode2Buffer : Streams.Buffer
{
    private readonly byte[] _iobuffer;
    private readonly IBuffer _wrapped;

    public Mode2Buffer(IBuffer toWrap)
    {
        _wrapped = toWrap;
        _iobuffer = new byte[DiscImageFile.Mode2SectorSize];
    }

    public override bool CanRead => true;

    public override bool CanWrite => false;

    public override long Capacity => _wrapped.Capacity / DiscImageFile.Mode2SectorSize * DiscImageFile.Mode1SectorSize;

    public override IEnumerable<StreamExtent> Extents
        => SingleValueEnumerable.Get(new StreamExtent(0, Capacity));

    public override int Read(long pos, byte[] buffer, int offset, int count)
    {
        var totalToRead = (int)Math.Min(Capacity - pos, count);
        var totalRead = 0;

        while (totalRead < totalToRead)
        {
            var thisPos = pos + totalRead;
            var sector = thisPos / DiscImageFile.Mode1SectorSize;
            var sectorOffset = (int)(thisPos - sector * DiscImageFile.Mode1SectorSize);

            _wrapped.ReadExactly(sector * DiscImageFile.Mode2SectorSize, _iobuffer, 0, DiscImageFile.Mode2SectorSize);

            var bytesToCopy = Math.Min(DiscImageFile.Mode1SectorSize - sectorOffset, totalToRead - totalRead);
            System.Buffer.BlockCopy(_iobuffer, 24 + sectorOffset, buffer, offset + totalRead, bytesToCopy);
            totalRead += bytesToCopy;
        }

        return totalRead;
    }

    public override async ValueTask<int> ReadAsync(long pos, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var totalToRead = (int)Math.Min(Capacity - pos, buffer.Length);
        var totalRead = 0;

        while (totalRead < totalToRead)
        {
            var thisPos = pos + totalRead;
            var sector = thisPos / DiscImageFile.Mode1SectorSize;
            var sectorOffset = (int)(thisPos - sector * DiscImageFile.Mode1SectorSize);

            await _wrapped.ReadExactlyAsync(sector * DiscImageFile.Mode2SectorSize, _iobuffer.AsMemory(0, DiscImageFile.Mode2SectorSize), cancellationToken).ConfigureAwait(false);

            var bytesToCopy = Math.Min(DiscImageFile.Mode1SectorSize - sectorOffset, totalToRead - totalRead);
            _iobuffer.AsMemory(24 + sectorOffset, bytesToCopy).CopyTo(buffer.Slice(totalRead));
            totalRead += bytesToCopy;
        }

        return totalRead;
    }

    public override int Read(long pos, Span<byte> buffer)
    {
        var totalToRead = (int)Math.Min(Capacity - pos, buffer.Length);
        var totalRead = 0;

        while (totalRead < totalToRead)
        {
            var thisPos = pos + totalRead;
            var sector = thisPos / DiscImageFile.Mode1SectorSize;
            var sectorOffset = (int)(thisPos - sector * DiscImageFile.Mode1SectorSize);

            _wrapped.ReadExactly(sector * DiscImageFile.Mode2SectorSize, _iobuffer, 0, DiscImageFile.Mode2SectorSize);

            var bytesToCopy = Math.Min(DiscImageFile.Mode1SectorSize - sectorOffset, totalToRead - totalRead);
            _iobuffer.AsSpan(24 + sectorOffset, bytesToCopy).CopyTo(buffer.Slice(totalRead));
            totalRead += bytesToCopy;
        }

        return totalRead;
    }

    public override void Write(long pos, byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override void Write(long pos, ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException();

    public override void Clear(long pos, int count)
    {
        throw new NotSupportedException();
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override void SetCapacity(long value)
    {
        throw new NotSupportedException();
    }

    public override IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
    {
        var capacity = Capacity;
        if (start < capacity)
        {
            var end = Math.Min(start + count, capacity);
            return SingleValueEnumerable.Get(new StreamExtent(start, end - start));
        }

        return Enumerable.Empty<StreamExtent>();
    }
}