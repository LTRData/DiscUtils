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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Iso9660;

internal class FileExtent : BuilderExtent
{
    private readonly BuildFileInfo _fileInfo;

    private Stream _readStream;

    public FileExtent(BuildFileInfo fileInfo, long start)
        : base(start, fileInfo.GetDataSize(Encoding.ASCII))
    {
        _fileInfo = fileInfo;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _readStream != null)
        {
            _fileInfo.CloseStream(_readStream);
            _readStream = null;
        }
    }

    public override void PrepareForRead()
    {
        _readStream = _fileInfo.OpenStream();
    }

    public override int Read(long diskOffset, byte[] block, int offset, int count)
    {
        var relPos = diskOffset - Start;
        var totalRead = 0;

        // Don't arbitrarily set position, just in case stream implementation is
        // non-seeking, and we're doing sequential reads
        if (_readStream.Position != relPos)
        {
            _readStream.Position = relPos;
        }

        // Read up to EOF
        var numRead = _readStream.Read(block, offset, count);
        totalRead += numRead;
        while (numRead > 0 && totalRead < count)
        {
            numRead = _readStream.Read(block, offset + totalRead, count - totalRead);
            totalRead += numRead;
        }

        return totalRead;
    }

    public override async ValueTask<int> ReadAsync(long diskOffset, byte[] block, int offset, int count, CancellationToken cancellationToken)
    {
        var relPos = diskOffset - Start;
        var totalRead = 0;

        // Don't arbitrarily set position, just in case stream implementation is
        // non-seeking, and we're doing sequential reads
        if (_readStream.Position != relPos)
        {
            _readStream.Position = relPos;
        }

        // Read up to EOF
        var numRead = await _readStream.ReadAsync(block.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        totalRead += numRead;
        while (numRead > 0 && totalRead < count)
        {
            numRead = await _readStream.ReadAsync(block.AsMemory(offset + totalRead, count - totalRead), cancellationToken).ConfigureAwait(false);
            totalRead += numRead;
        }

        return totalRead;
    }

    public override async ValueTask<int> ReadAsync(long diskOffset, Memory<byte> block, CancellationToken cancellationToken)
    {
        var relPos = diskOffset - Start;
        var totalRead = 0;

        // Don't arbitrarily set position, just in case stream implementation is
        // non-seeking, and we're doing sequential reads
        if (_readStream.Position != relPos)
        {
            _readStream.Position = relPos;
        }

        // Read up to EOF
        var numRead = await _readStream.ReadAsync(block, cancellationToken).ConfigureAwait(false);
        totalRead += numRead;
        while (numRead > 0 && totalRead < block.Length)
        {
            numRead = await _readStream.ReadAsync(block[totalRead..], cancellationToken).ConfigureAwait(false);
            totalRead += numRead;
        }

        return totalRead;
    }

    public override int Read(long diskOffset, Span<byte> block)
    {
        var relPos = diskOffset - Start;
        var totalRead = 0;

        // Don't arbitrarily set position, just in case stream implementation is
        // non-seeking, and we're doing sequential reads
        if (_readStream.Position != relPos)
        {
            _readStream.Position = relPos;
        }

        // Read up to EOF
        var numRead = _readStream.Read(block);
        totalRead += numRead;
        while (numRead > 0 && totalRead < block.Length)
        {
            numRead = _readStream.Read(block[totalRead..]);
            totalRead += numRead;
        }

        return totalRead;
    }

    public override void DisposeReadState()
    {
        _fileInfo.CloseStream(_readStream);
        _readStream = null;
    }
}