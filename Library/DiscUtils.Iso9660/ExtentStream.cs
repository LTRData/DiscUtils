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

using DiscUtils.Streams.Compatibility;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils.Iso9660;

internal class ExtentStream : ReadOnlyCompatibilityStream
{
    private readonly uint _dataLength;
    private readonly byte _fileUnitSize;
    private readonly byte _interleaveGapSize;

    private readonly Stream _isoStream;
    private long _position;

    private readonly uint _startBlock;

    public ExtentStream(Stream isoStream, uint startBlock, uint dataLength, byte fileUnitSize,
                        byte interleaveGapSize)
    {
        _isoStream = isoStream;
        _startBlock = startBlock;
        _dataLength = dataLength;
        _fileUnitSize = fileUnitSize;
        _interleaveGapSize = interleaveGapSize;

        if (_fileUnitSize != 0 || _interleaveGapSize != 0)
        {
            throw new NotSupportedException("Non-contiguous extents not supported");
        }
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override long Length => _dataLength;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position > _dataLength)
        {
            return 0;
        }

        var toRead = (int)Math.Min((uint)count, _dataLength - _position);

        _isoStream.Position = _position + _startBlock * (long)IsoUtilities.SectorSize;
        var numRead = _isoStream.Read(buffer, offset, toRead);
        _position += numRead;
        return numRead;
    }

    public override int Read(Span<byte> buffer)
    {
        if (_position > _dataLength)
        {
            return 0;
        }

        var toRead = (int)Math.Min((uint)buffer.Length, _dataLength - _position);

        _isoStream.Position = _position + _startBlock * (long)IsoUtilities.SectorSize;
        var numRead = _isoStream.Read(buffer.Slice(0, toRead));
        _position += numRead;
        return numRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (_position > _dataLength)
        {
            return 0;
        }

        var toRead = (int)Math.Min((uint)buffer.Length, _dataLength - _position);

        _isoStream.Position = _position + _startBlock * (long)IsoUtilities.SectorSize;
        var numRead = await _isoStream.ReadAsync(buffer.Slice(0, toRead), cancellationToken).ConfigureAwait(false);
        _position += numRead;
        return numRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var newPos = offset;
        if (origin == SeekOrigin.Current)
        {
            newPos += _position;
        }
        else if (origin == SeekOrigin.End)
        {
            newPos += _dataLength;
        }

        _position = newPos;
        return newPos;
    }
}