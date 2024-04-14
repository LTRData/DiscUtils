//
// Copyright (c) 2017, Bianco Veigel
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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Streams;

/// <summary>
/// Stream wrapper to allow forward only seeking on not seekable streams
/// </summary>
public class PositionWrappingStream : WrappingStream
{
    public PositionWrappingStream(SparseStream toWrap, long currentPosition, Ownership ownership)
        : base(toWrap, ownership)
    {
        _position = currentPosition;
    }

    private long _position;
    public override long Position
    {
        get => _position;
        set
        {
            if (_position == value)
            {
                return;
            }

            Seek(value, SeekOrigin.Begin);
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        if (base.CanSeek)
        {
            return base.Seek(offset, SeekOrigin.Current);
        }

        offset = origin switch
        {
            SeekOrigin.Begin => offset - _position,
            SeekOrigin.Current => offset + _position,
            SeekOrigin.End => Length - offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null),
        };
        if (offset == 0)
        {
            return _position;
        }

        if (offset < 0)
        {
            throw new NotSupportedException("backward seeking is not supported");
        }

        var buffer = new byte[Sizes.OneKiB];
        while (offset > 0)
        {
            var read = base.Read(buffer, 0, (int)Math.Min(buffer.Length, offset));
            offset -= read;
        }

        return _position;
    }

    public override bool CanSeek => true;

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = base.Read(buffer, offset, count);
        _position += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _position += read;
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = base.Read(buffer);
        _position += read;
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        base.Write(buffer, offset, count);
        _position += count;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await base.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        _position += count;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        _position += buffer.Length;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        base.Write(buffer);
        _position += buffer.Length;
    }

}