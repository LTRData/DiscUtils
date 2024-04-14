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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Nfs;

internal sealed class Nfs3FileStream : SparseStream
{
    private readonly FileAccess _access;
    private readonly Nfs3Client _client;
    private readonly Nfs3FileHandle _handle;

    private long _length;
    private long _position;

    public Nfs3FileStream(Nfs3Client client, Nfs3FileHandle handle, FileAccess access)
    {
        _client = client;
        _handle = handle;
        _access = access;

        _length = _client.GetAttributes(_handle).Size;
    }

    public override bool CanRead => _access != FileAccess.Write;

    public override bool CanSeek => true;

    public override bool CanWrite => _access != FileAccess.Read;

    public override IEnumerable<StreamExtent> Extents
        => SingleValueEnumerable.Get(new StreamExtent(0, Length));

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override void Flush() {}

    public override int Read(byte[] buffer, int offset, int count)
    {
        var numToRead = (int)Math.Min(_client.FileSystemInfo.ReadMaxBytes, count);
        var readResult = _client.Read(_handle, _position, numToRead);

        var toCopy = Math.Min(count, readResult.Count);

        System.Buffer.BlockCopy(readResult.Data, 0, buffer, offset, toCopy);

        if (readResult.Eof)
        {
            _length = _position + readResult.Count;
        }

        _position += toCopy;
        return toCopy;
    }

    public override int Read(Span<byte> buffer)
    {
        var numToRead = (int)Math.Min(_client.FileSystemInfo.ReadMaxBytes, buffer.Length);
        var readResult = _client.Read(_handle, _position, numToRead);

        var toCopy = Math.Min(buffer.Length, readResult.Count);

        readResult.Data.AsSpan(0, toCopy).CopyTo(buffer);

        if (readResult.Eof)
        {
            _length = _position + readResult.Count;
        }

        _position += toCopy;
        return toCopy;
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
            newPos += Length;
        }

        _position = newPos;
        return newPos;
    }

    public override void SetLength(long value)
    {
        if (CanWrite)
        {
            _client.SetAttributes(_handle, new Nfs3SetAttributes { SetSize = true, Size = value });
            _length = value;
        }
        else
        {
            throw new InvalidOperationException("Attempt to change length of read-only file");
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var totalWritten = 0;

        while (totalWritten < count)
        {
            var numToWrite = (int)Math.Min(_client.FileSystemInfo.WriteMaxBytes, (uint)(count - totalWritten));

            var numWritten = _client.Write(_handle, _position, buffer, offset + totalWritten, numToWrite);

            _position += numWritten;
            totalWritten += numWritten;
        }

        _length = Math.Max(_length, _position);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var totalWritten = 0;

        while (totalWritten < buffer.Length)
        {
            var numToWrite = (int)Math.Min(_client.FileSystemInfo.WriteMaxBytes, (uint)(buffer.Length - totalWritten));

            var numWritten = _client.Write(_handle, _position, buffer.Slice(totalWritten, numToWrite));

            _position += numWritten;
            totalWritten += numWritten;
        }

        _length = Math.Max(_length, _position);
    }

    public override System.Threading.Tasks.Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var totalWritten = 0;

        while (totalWritten < count)
        {
            var numToWrite = (int)Math.Min(_client.FileSystemInfo.WriteMaxBytes, (uint)(count - totalWritten));

            var numWritten = _client.Write(_handle, _position, buffer, offset + totalWritten, numToWrite);

            _position += numWritten;
            totalWritten += numWritten;
        }

        _length = Math.Max(_length, _position);

        return System.Threading.Tasks.Task.CompletedTask;
    }
}