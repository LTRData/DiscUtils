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
using K4os.Compression.LZ4;

namespace LibraryTests.SquashFs;

/// <summary>
/// A simple stream that uses LZ4 to compress and decompress data.
/// Read and Write methods expect the whole buffer to be read or written as we can only rely on LZ4 standard decode/encode buffer methods.
/// </summary>
internal sealed class SimpleLz4Stream : Stream
{
    private MemoryStream _inner;

    public SimpleLz4Stream(MemoryStream inner)
    {
        _inner = inner;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_inner.Position == _inner.Length)
        {
            return 0;
        }
        // This is supposed to be called only once
        var srcBuffer = _inner.GetBuffer();
        var decompressed = LZ4Codec.Decode(srcBuffer, (int)_inner.Position, (int)(_inner.Length - _inner.Position), buffer, offset, count);
        _inner.Position = _inner.Length;
        return decompressed;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.SetLength(count);
        var destBuffer = _inner.GetBuffer();
        var compressed = LZ4Codec.Encode(buffer, offset, count, destBuffer, 0, destBuffer.Length);
        // If the dest buffer is too small, fake that we wrote the whole buffer
        // In that case, the compressed buffer won't be use because it is at least equal to the uncompressed size.
        if (compressed < 0)
        {
            compressed = count;
        }
        _inner.SetLength(compressed);
        _inner.Position = compressed;
    }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    public override int Read(Span<byte> buffer)
    {
        if (_inner.Position == _inner.Length)
        {
            return 0;
        }
        var srcBuffer = _inner.ToArray();
        var decompressed = LZ4Codec.Decode(new ReadOnlySpan<byte>(srcBuffer, (int)_inner.Position, (int)(_inner.Length - _inner.Position)), buffer);
        _inner.Position = _inner.Length;
        return decompressed;
    }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _inner.SetLength(buffer.Length);
        var destBuffer = _inner.GetBuffer();
        var compressed = LZ4Codec.Encode(buffer, destBuffer);
        // If the dest buffer is too small, fake that we wrote the whole buffer
        // In that case, the compressed buffer won't be use because it is at least equal to the uncompressed size.
        if (compressed < 0)
        {
            compressed = buffer.Length;
        }
        _inner.SetLength(compressed);
        _inner.Position = compressed;
    }
#endif

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => _inner.CanWrite;

    public override long Length => _inner.Length;

    public override long Position { get; set; }
}
