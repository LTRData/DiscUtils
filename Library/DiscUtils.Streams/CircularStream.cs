//
// Copyright (c) 2008-2013, Kenneth Bell
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
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Streams;

/// <summary>
/// Represents a stream that is circular, so reads and writes off the end of the stream wrap.
/// </summary>
public sealed class CircularStream : WrappingStream
{
    public CircularStream(SparseStream toWrap, Ownership ownership)
        : base(toWrap, ownership) { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        WrapPosition();

        var read = base.Read(buffer, offset, (int)Math.Min(Length - Position, count));

        WrapPosition();

        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        WrapPosition();

        var read = await base.ReadAsync(buffer.Slice(0, (int)Math.Min(Length - Position, buffer.Length)), cancellationToken).ConfigureAwait(false);

        WrapPosition();

        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        WrapPosition();

        var read = base.Read(buffer.Slice(0, (int)Math.Min(Length - Position, buffer.Length)));

        WrapPosition();

        return read;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WrapPosition();

        var totalWritten = 0;
        while (totalWritten < count)
        {
            var toWrite = (int)Math.Min(count - totalWritten, Length - Position);

            base.Write(buffer, offset + totalWritten, toWrite);

            WrapPosition();

            totalWritten += toWrite;
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        WrapPosition();

        var totalWritten = 0;
        while (totalWritten < buffer.Length)
        {
            var toWrite = (int)Math.Min(buffer.Length - totalWritten, Length - Position);

            await base.WriteAsync(buffer.Slice(totalWritten, toWrite), cancellationToken).ConfigureAwait(false);

            WrapPosition();

            totalWritten += toWrite;
        }
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        WrapPosition();

        var totalWritten = 0;
        while (totalWritten < buffer.Length)
        {
            var toWrite = (int)Math.Min(buffer.Length - totalWritten, Length - Position);

            base.Write(buffer.Slice(totalWritten, toWrite));

            WrapPosition();

            totalWritten += toWrite;
        }
    }

    private void WrapPosition()
    {
        var pos = Position;
        var length = Length;

        if (pos >= length)
        {
            Position = pos % length;
        }
    }
}