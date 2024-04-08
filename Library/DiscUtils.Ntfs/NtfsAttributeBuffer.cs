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
using Buffer=DiscUtils.Streams.Buffer;

namespace DiscUtils.Ntfs;

internal class NtfsAttributeBuffer : Buffer, IMappedBuffer
{
    private readonly NtfsAttribute _attribute;
    private readonly File _file;

    public NtfsAttributeBuffer(File file, NtfsAttribute attribute)
    {
        _file = file;
        _attribute = attribute;
    }

    public override bool CanRead
    {
        get { return true; }
    }

    public override bool CanWrite
    {
        get { return _file.Context.RawStream.CanWrite; }
    }

    public override long Capacity
    {
        get { return _attribute.PrimaryRecord.DataLength; }
    }

    public long MapPosition(long pos)
    {
        if (_attribute.IsNonResident)
        {
            return ((IMappedBuffer)_attribute.RawBuffer).MapPosition(pos);
        }

        var attrRef = new AttributeReference(_file.MftReference,
            _attribute.PrimaryRecord.AttributeId);
        var attrRecord = (ResidentAttributeRecord)_file.GetAttribute(attrRef).PrimaryRecord;

        var attrStart = _file.GetAttributeOffset(attrRef);
        var mftPos = attrStart + attrRecord.DataOffset + pos;

        return
            _file.Context.GetFileByIndex(MasterFileTable.MftIndex)
                 .GetAttribute(AttributeType.Data, null)
                 .OffsetToAbsolutePos(mftPos);
    }

    public override int Read(long pos, byte[] buffer, int offset, int count)
    {
        var record = _attribute.PrimaryRecord;

        if (!CanRead)
        {
            throw new IOException("Attempt to read from file not opened for read");
        }

        StreamUtilities.AssertBufferParameters(buffer, offset, count);

        if (pos >= Capacity)
        {
            return 0;
        }

        // Limit read to length of attribute
        var totalToRead = (int)Math.Min(count, Capacity - pos);
        var toRead = totalToRead;

        // Handle uninitialized bytes at end of attribute
        if (pos + totalToRead > record.InitializedDataLength)
        {
            if (pos >= record.InitializedDataLength)
            {
                // We're just reading zero bytes from the uninitialized area
                Array.Clear(buffer, offset, totalToRead);
                return totalToRead;
            }

            // Partial read of uninitialized area
            Array.Clear(buffer, offset + (int)(record.InitializedDataLength - pos),
                (int)(pos + toRead - record.InitializedDataLength));
            toRead = (int)(record.InitializedDataLength - pos);
        }

        var numRead = 0;
        while (numRead < toRead)
        {
            var extentBuffer = _attribute.RawBuffer;

            var justRead = extentBuffer.Read(pos + numRead, buffer, offset + numRead, toRead - numRead);
            if (justRead == 0)
            {
                break;
            }

            numRead += justRead;
        }

        return totalToRead;
    }

    public override async ValueTask<int> ReadAsync(long pos, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var record = _attribute.PrimaryRecord;

        if (!CanRead)
        {
            throw new IOException("Attempt to read from file not opened for read");
        }

        if (pos >= Capacity)
        {
            return 0;
        }

        // Limit read to length of attribute
        var totalToRead = (int)Math.Min(buffer.Length, Capacity - pos);
        var toRead = totalToRead;

        // Handle uninitialized bytes at end of attribute
        if (pos + totalToRead > record.InitializedDataLength)
        {
            if (pos >= record.InitializedDataLength)
            {
                // We're just reading zero bytes from the uninitialized area
                buffer.Span.Slice(0, totalToRead).Clear();
                return totalToRead;
            }

            // Partial read of uninitialized area
            buffer.Span.Slice((int)(record.InitializedDataLength - pos),
                (int)(pos + toRead - record.InitializedDataLength)).Clear();
            toRead = (int)(record.InitializedDataLength - pos);
        }

        var numRead = 0;
        while (numRead < toRead)
        {
            var extentBuffer = _attribute.RawBuffer;

            var justRead = await extentBuffer.ReadAsync(pos + numRead, buffer.Slice(numRead, toRead - numRead), cancellationToken).ConfigureAwait(false);
            if (justRead == 0)
            {
                break;
            }

            numRead += justRead;
        }

        return totalToRead;
    }

    public override int Read(long pos, Span<byte> buffer)
    {
        var record = _attribute.PrimaryRecord;

        if (!CanRead)
        {
            throw new IOException("Attempt to read from file not opened for read");
        }

        if (pos >= Capacity)
        {
            return 0;
        }

        // Limit read to length of attribute
        var totalToRead = (int)Math.Min(buffer.Length, Capacity - pos);
        var toRead = totalToRead;

        // Handle uninitialized bytes at end of attribute
        if (pos + totalToRead > record.InitializedDataLength)
        {
            if (pos >= record.InitializedDataLength)
            {
                // We're just reading zero bytes from the uninitialized area
                buffer.Slice(0, totalToRead).Clear();
                return totalToRead;
            }

            // Partial read of uninitialized area
            buffer.Slice((int)(record.InitializedDataLength - pos),
                (int)(pos + toRead - record.InitializedDataLength)).Clear();
            toRead = (int)(record.InitializedDataLength - pos);
        }

        var numRead = 0;
        while (numRead < toRead)
        {
            var extentBuffer = _attribute.RawBuffer;

            var justRead = extentBuffer.Read(pos + numRead, buffer.Slice(numRead, toRead - numRead));
            if (justRead == 0)
            {
                break;
            }

            numRead += justRead;
        }

        return totalToRead;
    }

    public override void SetCapacity(long value)
    {
        if (!CanWrite)
        {
            throw new IOException("Attempt to change length of file not opened for write");
        }

        if (value == Capacity)
        {
            return;
        }

        _attribute.RawBuffer.SetCapacity(value);
        _file.MarkMftRecordDirty();
    }

    public override void Write(long pos, byte[] buffer, int offset, int count)
    {
        var record = _attribute.PrimaryRecord;

        if (!CanWrite)
        {
            throw new IOException("Attempt to write to file not opened for write");
        }

        StreamUtilities.AssertBufferParameters(buffer, offset, count);

        if (count == 0)
        {
            return;
        }

        _attribute.RawBuffer.Write(pos, buffer, offset, count);

        if (!record.IsNonResident)
        {
            _file.MarkMftRecordDirty();
        }
    }

    public override async ValueTask WriteAsync(long pos, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var record = _attribute.PrimaryRecord;

        if (!CanWrite)
        {
            throw new IOException("Attempt to write to file not opened for write");
        }

        if (buffer.Length == 0)
        {
            return;
        }

        await _attribute.RawBuffer.WriteAsync(pos, buffer, cancellationToken).ConfigureAwait(false);

        if (!record.IsNonResident)
        {
            _file.MarkMftRecordDirty();
        }
    }

    public override void Write(long pos, ReadOnlySpan<byte> buffer)
    {
        var record = _attribute.PrimaryRecord;

        if (!CanWrite)
        {
            throw new IOException("Attempt to write to file not opened for write");
        }

        if (buffer.Length == 0)
        {
            return;
        }

        _attribute.RawBuffer.Write(pos, buffer);

        if (!record.IsNonResident)
        {
            _file.MarkMftRecordDirty();
        }
    }

    public override void Clear(long pos, int count)
    {
        var record = _attribute.PrimaryRecord;

        if (!CanWrite)
        {
            throw new IOException("Attempt to write to file not opened for write");
        }

        if (count == 0)
        {
            return;
        }

        _attribute.RawBuffer.Clear(pos, count);

        if (!record.IsNonResident)
        {
            _file.MarkMftRecordDirty();
        }
    }

    public override IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
    {
        return StreamExtent.Intersect(_attribute.RawBuffer.GetExtentsInRange(start, count),
            new StreamExtent(0, Capacity));
    }
}