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

//
// Based on "libbzip2", Copyright (C) 1996-2007 Julian R Seward.
//

using DiscUtils.Streams.Compatibility;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Compression;

internal class BZip2RleStream : ReadOnlyCompatibilityStream
{
    private byte[] _blockBuffer;
    private int _blockOffset;
    private int _blockRemaining;
    private byte _lastByte;

    private int _numSame;
    private long _position;
    private int _runBytesOutstanding;

    public bool AtEof => _runBytesOutstanding == 0 && _blockRemaining == 0;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public void Reset(byte[] buffer, int offset, int count)
    {
        _position = 0;
        _blockBuffer = buffer;
        _blockOffset = offset;
        _blockRemaining = count;
        _numSame = -1;
        _lastByte = 0;
        _runBytesOutstanding = 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => new(Read(buffer.Span));

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var numRead = 0;

        while (numRead < buffer.Length && _runBytesOutstanding > 0)
        {
            var runCount = Math.Min(_runBytesOutstanding, buffer.Length);
            for (var i = 0; i < runCount; ++i)
            {
                buffer[numRead] = _lastByte;
            }

            _runBytesOutstanding -= runCount;
            numRead += runCount;
        }

        while (numRead < buffer.Length && _blockRemaining > 0)
        {
            var b = _blockBuffer[_blockOffset];
            ++_blockOffset;
            --_blockRemaining;

            if (_numSame == 4)
            {
                var runCount = Math.Min(b, buffer.Length - numRead);
                for (var i = 0; i < runCount; ++i)
                {
                    buffer[numRead] = _lastByte;
                    numRead++;
                }

                _runBytesOutstanding = b - runCount;
                _numSame = 0;
            }
            else
            {
                if (b != _lastByte || _numSame <= 0)
                {
                    _lastByte = b;
                    _numSame = 0;
                }

                buffer[numRead] = b;
                numRead++;
                _numSame++;
            }
        }

        _position += numRead;
        return numRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }
}