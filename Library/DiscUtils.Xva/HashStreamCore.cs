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
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

#if NETSTANDARD || NETCOREAPP || NET461_OR_GREATER
internal class HashStreamCore : CompatibilityStream
{
    private readonly IncrementalHash _hashAlg;
    private readonly Ownership _ownWrapped;

    private long _hashPos;
    private Stream _wrapped;

    public HashStreamCore(Stream wrapped, Ownership ownsWrapped, IncrementalHash hashAlg)
    {
        _wrapped = wrapped;
        _ownWrapped = ownsWrapped;
        _hashAlg = hashAlg;
    }

    public override bool CanRead => _wrapped.CanRead;

    public override bool CanSeek => _wrapped.CanSeek;

    public override bool CanWrite => _wrapped.CanWrite;

    public override long Length => _wrapped.Length;

    public override long Position
    {
        get => _wrapped.Position;

        set => _wrapped.Position = value;
    }

    public override void Flush()
    {
        _wrapped.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (Position != _hashPos)
        {
            throw new InvalidOperationException("Reads must be contiguous");
        }

        var numRead = _wrapped.Read(buffer, offset, count);

        _hashAlg.AppendData(buffer, offset, numRead);
        _hashPos += numRead;

        return numRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (Position != _hashPos)
        {
            throw new InvalidOperationException("Reads must be contiguous");
        }

        var numRead = await _wrapped.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        _hashAlg.AppendData(buffer.Span.Slice(0, numRead));
        _hashPos += numRead;

        return numRead;
    }

    public override int Read(Span<byte> buffer)
    {
        if (Position != _hashPos)
        {
            throw new InvalidOperationException("Reads must be contiguous");
        }

        var numRead = _wrapped.Read(buffer);

        _hashAlg.AppendData(buffer.Slice(0, numRead));
        _hashPos += numRead;

        return numRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _wrapped.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _wrapped.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _wrapped.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _wrapped.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _wrapped.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _wrapped.WriteAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && _ownWrapped == Ownership.Dispose && _wrapped != null)
            {
                _wrapped.Dispose();
                _wrapped = null;
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }
}
#else
internal class HashStreamDotnet : CompatibilityStream
{
    private Stream _wrapped;
    private Ownership _ownWrapped;

    private HashAlgorithm _hashAlg;

    private long _hashPos;

    public HashStreamDotnet(Stream wrapped, Ownership ownsWrapped, HashAlgorithm hashAlg)
    {
        _wrapped = wrapped;
        _ownWrapped = ownsWrapped;
        _hashAlg = hashAlg;
    }

    public override bool CanRead => _wrapped.CanRead;

    public override bool CanSeek => _wrapped.CanSeek;

    public override bool CanWrite => _wrapped.CanWrite;

    public override long Length => _wrapped.Length;

    public override long Position
    {
        get => _wrapped.Position;

        set => _wrapped.Position = value;
    }

    public override void Flush()
    {
        _wrapped.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (Position != _hashPos)
        {
            throw new InvalidOperationException("Reads must be contiguous");
        }

        var numRead = _wrapped.Read(buffer, offset, count);

        _hashAlg.TransformBlock(buffer, offset, numRead, buffer, offset);
        _hashPos += numRead;

        return numRead;
    }

    public override int Read(Span<byte> buffer) =>
        CompatExtensions.ReadUsingArray(this, buffer);

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _wrapped.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _wrapped.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _wrapped.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer) => _wrapped.Write(buffer);

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _wrapped.FlushAsync(cancellationToken);

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (Position != _hashPos)
        {
            throw new InvalidOperationException("Reads must be contiguous");
        }

        var numRead = await _wrapped.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);

        _hashAlg.TransformBlock(buffer, offset, numRead, buffer, offset);
        _hashPos += numRead;

        return numRead;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        CompatExtensions.ReadUsingArrayAsync(this, buffer, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _wrapped.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _wrapped.WriteAsync(buffer, cancellationToken);

    public override void WriteByte(byte value) => _wrapped.WriteByte(value);

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && _ownWrapped == Ownership.Dispose && _wrapped != null)
            {
                _wrapped.Dispose();
                _wrapped = null;
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }
}
#endif
