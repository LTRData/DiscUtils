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
using System.Threading.Tasks;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Fat;

internal class FatFileStream : SparseStream
{
    private readonly Directory _dir;
    private readonly long _dirId;
    private readonly ClusterStream _stream;

    private bool didWrite;

    public FatFileStream(FatFileSystem fileSystem, Directory dir, long fileId, FileAccess access)
    {
        _dir = dir;
        _dirId = fileId;

        var dirEntry = _dir.GetEntry(_dirId);
        _stream = new ClusterStream(fileSystem, access, dirEntry.FirstCluster, (uint)dirEntry.FileSize);
        _stream.FirstClusterChanged += FirstClusterAllocatedHandler;
    }

    public override bool CanRead => _stream.CanRead;

    public override bool CanSeek => _stream.CanSeek;

    public override bool CanWrite => _stream.CanWrite;

    public override IEnumerable<StreamExtent> Extents
        => SingleValueEnumerable.Get(new StreamExtent(0, Length));

    public override long Length => _stream.Length;

    public override long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && _dir.FileSystem.CanWrite)
            {
                var now = _dir.FileSystem.ConvertFromUtc(DateTime.UtcNow);

                var dirEntry = _dir.GetEntry(_dirId);
                dirEntry.LastAccessTime = now;
                if (didWrite)
                {
                    dirEntry.FileSize = (int)_stream.Length;
                    dirEntry.LastWriteTime = now;
                }

                _dir.UpdateEntry(_dirId, dirEntry);
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    public override void SetLength(long value)
    {
        didWrite = true;
        _stream.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        didWrite = true;
        _stream.Write(buffer, offset, count);
    }

    public override void Flush()
    {
        _stream.Flush();
    }

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
        didWrite = true;
        return _stream.BeginWrite(buffer, offset, count, callback, state);
    }

    public override void EndWrite(IAsyncResult asyncResult) => _stream.EndWrite(asyncResult);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        didWrite = true;
        return _stream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => _stream.FlushAsync(cancellationToken);

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        didWrite = true;
        _stream.Write(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        didWrite = true;
        return _stream.WriteAsync(buffer, cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _stream.Read(buffer, offset, count);
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state) =>
        _stream.BeginRead(buffer, offset, count, callback, state);

    public override int EndRead(IAsyncResult asyncResult) =>
        _stream.EndRead(asyncResult);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _stream.ReadAsync(buffer, offset, count, cancellationToken);

    public override int Read(Span<byte> buffer) =>
        _stream.Read(buffer);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _stream.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _stream.Seek(offset, origin);
    }

    private void FirstClusterAllocatedHandler(uint cluster)
    {
        var dirEntry = _dir.GetEntry(_dirId);
        dirEntry.FirstCluster = cluster;
        _dir.UpdateEntry(_dirId, dirEntry);
    }

    public IEnumerable<Range<long, long>> EnumerateAllocatedClusters() => _stream.EnumerateAllocatedClusters();
}