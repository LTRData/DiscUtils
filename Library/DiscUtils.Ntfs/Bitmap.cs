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

namespace DiscUtils.Ntfs;

internal sealed class Bitmap : IDisposable
{
    private BlockCacheStream _bitmap;
    private readonly long _maxIndex;

    private long _nextAvailable;
    private readonly Stream _stream;

    public Bitmap(Stream stream, long maxIndex)
    {
        _stream = stream;
        _maxIndex = maxIndex;
        _bitmap = new BlockCacheStream(SparseStream.FromStream(stream, Ownership.None), Ownership.None);
    }

    public void Dispose()
    {
        if (_bitmap != null)
        {
            _bitmap.Dispose();
            _bitmap = null;
        }
    }

    public bool IsPresent(long index)
    {
        var byteIdx = index / 8;
        var mask = 1 << (int)(index % 8);
        return (GetByte(byteIdx) & mask) != 0;
    }

    public void MarkPresent(long index)
    {
        var byteIdx = index / 8;
        var mask = (byte)(1 << (byte)(index % 8));

        if (byteIdx >= _bitmap.Length)
        {
            _bitmap.Position = MathUtilities.RoundUp(byteIdx + 1, 8) - 1;
            _bitmap.WriteByte(0);
        }

        SetByte(byteIdx, (byte)(GetByte(byteIdx) | mask));
    }

    public void MarkPresentRange(long index, long count)
    {
        if (count <= 0)
        {
            return;
        }

        var firstByte = index / 8;
        var lastByte = (index + count - 1) / 8;

        if (lastByte >= _bitmap.Length)
        {
            _bitmap.Position = MathUtilities.RoundUp(lastByte + 1, 8) - 1;
            _bitmap.WriteByte(0);
        }

        var bufferSize = (int)(lastByte - firstByte + 1);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            Array.Clear(buffer, 0, bufferSize);

            buffer[0] = GetByte(firstByte);
            
            if (bufferSize != 1)
            {
                buffer[bufferSize - 1] = GetByte(lastByte);
            }

            for (var i = index; i < index + count; ++i)
            {
                var byteIdx = i / 8 - firstByte;
                var mask = (byte)(1 << (byte)(i % 8));

                buffer[byteIdx] |= mask;
            }

            SetBytes(firstByte, buffer, 0, bufferSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async ValueTask MarkPresentRangeAsync(long index, long count, CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return;
        }

        var firstByte = index / 8;
        var lastByte = (index + count - 1) / 8;

        if (lastByte >= _bitmap.Length)
        {
            _bitmap.Position = MathUtilities.RoundUp(lastByte + 1, 8) - 1;
            _bitmap.WriteByte(0);
        }

        var bufferSize = (int)(lastByte - firstByte + 1);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            Array.Clear(buffer, 0, bufferSize);

            buffer[0] = GetByte(firstByte);

            if (bufferSize != 1)
            {
                buffer[bufferSize - 1] = GetByte(lastByte);
            }

            for (var i = index; i < index + count; ++i)
            {
                var byteIdx = i / 8 - firstByte;
                var mask = (byte)(1 << (byte)(i % 8));

                buffer[byteIdx] |= mask;
            }

            await SetBytesAsync(firstByte, buffer.AsMemory(0, bufferSize), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void MarkAbsent(long index)
    {
        var byteIdx = index / 8;
        var mask = (byte)(1 << (byte)(index % 8));

        if (byteIdx < _stream.Length)
        {
            SetByte(byteIdx, (byte)(GetByte(byteIdx) & ~mask));
        }

        if (index < _nextAvailable)
        {
            _nextAvailable = index;
        }
    }

    internal void MarkAbsentRange(long index, long count)
    {
        if (count <= 0)
        {
            return;
        }

        var firstByte = index / 8;
        var lastByte = (index + count - 1) / 8;
        if (lastByte >= _bitmap.Length)
        {
            _bitmap.Position = MathUtilities.RoundUp(lastByte + 1, 8) - 1;
            _bitmap.WriteByte(0);
        }

        var bufferLength = (int)(lastByte - firstByte + 1);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferLength);
        try
        {
            buffer[0] = GetByte(firstByte);
            if (bufferLength != 1)
            {
                buffer[bufferLength - 1] = GetByte(lastByte);
            }

            for (var i = index; i < index + count; ++i)
            {
                var byteIdx = i / 8 - firstByte;
                var mask = (byte)(1 << (byte)(i % 8));

                buffer[byteIdx] &= (byte)~mask;
            }

            SetBytes(firstByte, buffer, 0, bufferLength);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (index < _nextAvailable)
        {
            _nextAvailable = index;
        }
    }

    internal async ValueTask MarkAbsentRangeAsync(long index, long count, CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return;
        }

        var firstByte = index / 8;
        var lastByte = (index + count - 1) / 8;
        if (lastByte >= _bitmap.Length)
        {
            _bitmap.Position = MathUtilities.RoundUp(lastByte + 1, 8) - 1;
            _bitmap.WriteByte(0);
        }

        var bufferLength = (int)(lastByte - firstByte + 1);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferLength);
        try
        {
            buffer[0] = GetByte(firstByte);
            if (bufferLength != 1)
            {
                buffer[bufferLength - 1] = GetByte(lastByte);
            }

            for (var i = index; i < index + count; ++i)
            {
                var byteIdx = i / 8 - firstByte;
                var mask = (byte)(1 << (byte)(i % 8));

                buffer[byteIdx] &= (byte)~mask;
            }

            await SetBytesAsync(firstByte, buffer.AsMemory(0, bufferLength), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (index < _nextAvailable)
        {
            _nextAvailable = index;
        }
    }

    internal long AllocateFirstAvailable(long minValue)
    {
        var i = Math.Max(minValue, _nextAvailable);
        while (IsPresent(i) && i < _maxIndex)
        {
            ++i;
        }

        if (i < _maxIndex)
        {
            MarkPresent(i);
            _nextAvailable = i + 1;
            return i;
        }

        return -1;
    }

    internal long SetTotalEntries(long numEntries)
    {
        var length = MathUtilities.RoundUp(MathUtilities.Ceil(numEntries, 8), 8);
        _stream.SetLength(length);
        return length * 8;
    }

    internal long Size => _bitmap.Length;

    internal byte GetByte(long index)
    {
        if (index >= _bitmap.Length)
        {
            return 0;
        }

        _bitmap.Position = index;

        var buffer = _bitmap.ReadByte();

        if (buffer >= 0)
        {
            return (byte)buffer;
        }

        return 0;
    }

    internal int GetBytes(long index, byte[] buffer, int offset, int count)
    {
        if (index + count >= _bitmap.Length)
        {
            count = (int)(_bitmap.Length - index);
        }

        if (count <= 0)
        {
            return 0;
        }

        _bitmap.Position = index;
        return _bitmap.Read(buffer, offset, count);
    }

    internal ValueTask<int> GetBytesAsync(long index, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (index + buffer.Length >= _bitmap.Length)
        {
            buffer = buffer.Slice(0, (int)(_bitmap.Length - index));
        }

        if (buffer.IsEmpty)
        {
            return new(0);
        }

        _bitmap.Position = index;
        return _bitmap.ReadAsync(buffer, cancellationToken);
    }

    private void SetByte(long index, byte value)
    {
        _bitmap.Position = index;
        _bitmap.WriteByte(value);
        _bitmap.Flush();
    }

    private void SetBytes(long index, byte[] buffer, int offset, int count)
    {
        _bitmap.Position = index;
        _bitmap.Write(buffer, offset, count);
        _bitmap.Flush();
    }

    private async ValueTask SetBytesAsync(long index, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        _bitmap.Position = index;
        await _bitmap.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await _bitmap.FlushAsync(cancellationToken);
    }
}
