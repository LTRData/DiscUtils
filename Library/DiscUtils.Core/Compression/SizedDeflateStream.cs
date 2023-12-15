//
// Copyright (c) 2014, Quamotion
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

using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Async;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Compression;

internal class SizedDeflateStream : DeflateStream
{
    private readonly int _length;
    private int _position;

    public SizedDeflateStream(Stream stream, CompressionMode mode, bool leaveOpen, int length)
        : base(stream, mode, leaveOpen)
    {
        _length = length;
    }

    public override long Length
    {
        get { return _length; }
    }

    public override long Position
    {
        get { return _position; }
        set
        {
            if (value != Position)
            {
                throw new NotImplementedException();
            }
        }
    }

    public override int Read(byte[] array, int offset, int count)
    {
        var read = base.Read(array, offset, count);
        _position += read;
        return read;
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).AsAsyncResult(callback, state);

    public override int EndRead(IAsyncResult asyncResult) => ((Task<int>)asyncResult).GetAwaiter().GetResult();

    public async override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        var read = await base.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
#else
        var read = await base.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
#endif
        _position += read;
        return read;
    }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    public override int Read(Span<byte> buffer)
    {
        var read = base.Read(buffer);
        _position += read;
        return read;
    }

    public async override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _position += read;
        return read;
    }
#endif

}
