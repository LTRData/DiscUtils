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

namespace DiscUtils.Ntfs;

internal sealed class NtfsFileStream : SparseStream
{
    public SparseStream BaseStream { get; private set; }

    private readonly DirectoryEntry _entry;

    private readonly File _file;

    private bool _isDirty;

    public static SparseStream Open(File file, DirectoryEntry entry, AttributeType attrType, string attrName,
                          FileAccess access)
    {
        var baseStream = file.OpenStream(attrType, attrName, access);

        if (baseStream is null)
        {
            return null;
        }

        if (file.Context.ReadOnly)
        {
            return baseStream;
        }
        else
        {
            return new NtfsFileStream(entry, file, baseStream);
        }
    }

    private NtfsFileStream(DirectoryEntry entry, File file, SparseStream baseStream)
    {
        _entry = entry;
        _file = file;
        BaseStream = baseStream;
    }

    public static SparseStream Open(File file, DirectoryEntry entry, AttributeType attrType, ushort attrId,
                          FileAccess access)
    {
        var baseStream = file.OpenStream(attrId, attrType, access);

        if (baseStream is null)
        {
            return null;
        }

        if (file.Context.ReadOnly)
        {
            return baseStream;
        }
        else
        {
            return new NtfsFileStream(entry, file, baseStream);
        }
    }

    public override long? GetPositionInBaseStream(Stream baseStream, long virtualPosition)
    {
        if (ReferenceEquals(baseStream, this))
        {
            return virtualPosition;
        }

        return BaseStream.GetPositionInBaseStream(baseStream, virtualPosition);
    }

    public override bool CanRead => BaseStream is not null && BaseStream.CanRead;

    public override bool CanSeek => BaseStream is not null && BaseStream.CanSeek;

    public override bool CanWrite => BaseStream is not null && BaseStream.CanWrite;

    public override IEnumerable<StreamExtent> Extents
    {
        get
        {
            AssertOpen();
            return BaseStream.Extents;
        }
    }

    public override long Length
    {
        get
        {
            AssertOpen();
            return BaseStream.Length;
        }
    }

    public override long Position
    {
        get
        {
            AssertOpen();
            return BaseStream.Position;
        }

        set
        {
            AssertOpen();
            using (NtfsTransaction.Begin())
            {
                BaseStream.Position = value;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (BaseStream == null)
        {
            base.Dispose(disposing);
            return;
        }

        if (disposing)
        {
            using (NtfsTransaction.Begin())
            {
                base.Dispose(disposing);
                BaseStream.Dispose();

                UpdateMetadata();

                BaseStream = null;
            }
        }
    }

    public override void Flush()
    {
        AssertOpen();
        using (NtfsTransaction.Begin())
        {
            BaseStream.Flush();

            UpdateMetadata();
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        AssertOpen();
        StreamUtilities.AssertBufferParameters(buffer, offset, count);

        using (NtfsTransaction.Begin())
        {
            return BaseStream.Read(buffer, offset, count);
        }
    }

    public override int Read(Span<byte> buffer)
    {
        AssertOpen();

        using (NtfsTransaction.Begin())
        {
            return BaseStream.Read(buffer);
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        AssertOpen();

        using (NtfsTransaction.Begin())
        {
            return await BaseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        AssertOpen();
        using (NtfsTransaction.Begin())
        {
            return BaseStream.Seek(offset, origin);
        }
    }

    public override void SetLength(long value)
    {
        AssertOpen();
        using (NtfsTransaction.Begin())
        {
            if (value != Length)
            {
                _isDirty = true;
                BaseStream.SetLength(value);
            }
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        AssertOpen();
        StreamUtilities.AssertBufferParameters(buffer, offset, count);

        using (NtfsTransaction.Begin())
        {
            _isDirty = true;
            BaseStream.Write(buffer, offset, count);
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        AssertOpen();
        StreamUtilities.AssertBufferParameters(buffer, offset, count);

        using (NtfsTransaction.Begin())
        {
            _isDirty = true;
            await BaseStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        }
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        AssertOpen();

        using (NtfsTransaction.Begin())
        {
            _isDirty = true;
            BaseStream.Write(buffer);
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        AssertOpen();

        using (NtfsTransaction.Begin())
        {
            _isDirty = true;
            await BaseStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
    }

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        BaseStream.FlushAsync(cancellationToken);

    public override void Clear(int count)
    {
        AssertOpen();
        using (NtfsTransaction.Begin())
        {
            _isDirty = true;
            BaseStream.Clear(count);
        }
    }

    private void UpdateMetadata()
    {
        if (!_file.Context.ReadOnly)
        {
            // Update the standard information attribute - so it reflects the actual file state
            if (_isDirty)
            {
                _file.Modified();
            }
            else
            {
                _file.Accessed();
            }

            // Update the directory entry used to open the file, so it's accurate
            _entry.UpdateFrom(_file);

            // Write attribute changes back to the Master File Table
            _file.UpdateRecordInMft();
            _isDirty = false;
        }
    }

    private void AssertOpen()
    {
#if NET7_0_OR_GREATER
        ObjectDisposedException.ThrowIf(BaseStream is null, this);
#else
        if (BaseStream is null)
        {
            throw new ObjectDisposedException(_entry.Details.FileName, "Attempt to use closed stream");
        }
#endif
    }
}