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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Nfs;

public sealed class XdrDataWriter : BigEndianDataWriter
{
    public XdrDataWriter(Stream stream)
        : base(stream) { }

    public void Write(bool value)
    {
        Write(value ? 1 : 0);
    }

    public override void WriteBytes(byte[] buffer, int offset, int count)
    {
        _stream.Write(buffer, offset, count);
        if ((count & 0x3) != 0)
        {
            Span<byte> padding = stackalloc byte[4 - (buffer.Length & 0x3)];
            padding.Clear();
            _stream.Write(padding);
        }
    }

    public override void WriteBytes(ReadOnlySpan<byte> buffer)
    {
        _stream.Write(buffer);
        if ((buffer.Length & 0x3) != 0)
        {
            Span<byte> padding = stackalloc byte[4 - (buffer.Length & 0x3)];
            padding.Clear();
            _stream.Write(padding);
        }
    }

    public override async ValueTask WriteBytesAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        if ((count & 0x3) != 0)
        {
            var paddingSize = 4 - (buffer.Length & 0x3);
            var padding = ArrayPool<byte>.Shared.Rent(paddingSize);
            try
            {
                Array.Clear(padding, 0, paddingSize);
                await _stream.WriteAsync(padding.AsMemory(0, paddingSize), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(padding);
            }
        }
    }

    public override async ValueTask WriteBytesAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        if ((buffer.Length & 0x3) != 0)
        {
            var paddingSize = 4 - (buffer.Length & 0x3);
            var padding = ArrayPool<byte>.Shared.Rent(paddingSize);
            try
            {
                Array.Clear(padding, 0, paddingSize);
                await _stream.WriteAsync(padding.AsMemory(0, paddingSize), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(padding);
            }
        }
    }

    public void WriteBuffer(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            Write(0);
        }
        else
        {
            Write(buffer.Length);
            WriteBytes(buffer);
        }
    }

    public void WriteBuffer(byte[] buffer, int offset, int count)
    {
        if (buffer == null || count == 0)
        {
            Write(0);
        }
        else
        {
            Write(count);
            WriteBytes(buffer, offset, count);
        }
    }

    public void Write(string value)
    {
        WriteBuffer(Encoding.ASCII.GetBytes(value));
    }
}