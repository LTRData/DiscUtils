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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Vdi;

internal class DiskStream : SparseStream
{
    private const uint BlockFree = unchecked((uint)~0);
    private const uint BlockZero = unchecked((uint)~1);
    private bool _atEof;

    private uint[] _blockTable;
    private readonly HeaderRecord _fileHeader;

    private Stream _fileStream;

    private bool _isDisposed;
    private readonly Ownership _ownsStream;

    private long _position;
    private bool _writeNotified;

    public DiskStream(Stream fileStream, Ownership ownsStream, HeaderRecord fileHeader)
    {
        _fileStream = fileStream;
        _fileHeader = fileHeader;

        _ownsStream = ownsStream;

        ReadBlockTable();
    }

    public override bool CanRead
    {
        get
        {
            CheckDisposed();
            return true;
        }
    }

    public override bool CanSeek
    {
        get
        {
            CheckDisposed();
            return true;
        }
    }

    public override bool CanWrite
    {
        get
        {
            CheckDisposed();
            return _fileStream.CanWrite;
        }
    }

    public override IEnumerable<StreamExtent> Extents
    {
        get
        {
            long blockSize = _fileHeader.BlockSize;
            var i = 0;
            while (i < _blockTable.Length)
            {
                // Find next stored block
                while (i < _blockTable.Length && (_blockTable[i] == BlockZero || _blockTable[i] == BlockFree))
                {
                    ++i;
                }

                var start = i;

                // Find next absent block
                while (i < _blockTable.Length && _blockTable[i] != BlockZero && _blockTable[i] != BlockFree)
                {
                    ++i;
                }

                if (start != i)
                {
                    yield return new StreamExtent(start * blockSize, (i - start) * blockSize);
                }
            }
        }
    }

    public override long Length
    {
        get
        {
            CheckDisposed();
            return _fileHeader.DiskSize;
        }
    }

    public override long Position
    {
        get
        {
            CheckDisposed();
            return _position;
        }

        set
        {
            CheckDisposed();
            _position = value;
            _atEof = false;
        }
    }

    public event EventHandler WriteOccurred;

    public override void Flush()
    {
        CheckDisposed();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        CheckDisposed();

        if (_atEof || _position > _fileHeader.DiskSize)
        {
            _atEof = true;
            throw new IOException("Attempt to read beyond end of file");
        }

        if (_position == _fileHeader.DiskSize)
        {
            _atEof = true;
            return 0;
        }

        var maxToRead = (int)Math.Min(count, _fileHeader.DiskSize - _position);
        var numRead = 0;

        while (numRead < maxToRead)
        {
            var block = (int)(_position / _fileHeader.BlockSize);
            var offsetInBlock = (int)(_position % _fileHeader.BlockSize);

            var toRead = Math.Min(maxToRead - numRead, _fileHeader.BlockSize - offsetInBlock);

            if (_blockTable[block] == BlockFree)
            {
                // TODO: Use parent
                Array.Clear(buffer, offset + numRead, toRead);
            }
            else if (_blockTable[block] == BlockZero)
            {
                Array.Clear(buffer, offset + numRead, toRead);
            }
            else
            {
                var blockOffset = _blockTable[block] * (_fileHeader.BlockSize + _fileHeader.BlockExtraSize);
                var filePos = _fileHeader.DataOffset + _fileHeader.BlockExtraSize + blockOffset +
                               offsetInBlock;
                _fileStream.Position = filePos;
                _fileStream.ReadExactly(buffer, offset + numRead, toRead);
            }

            _position += toRead;
            numRead += toRead;
        }

        return numRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        CheckDisposed();

        if (_atEof || _position > _fileHeader.DiskSize)
        {
            _atEof = true;
            throw new IOException("Attempt to read beyond end of file");
        }

        if (_position == _fileHeader.DiskSize)
        {
            _atEof = true;
            return 0;
        }

        var maxToRead = (int)Math.Min(buffer.Length, _fileHeader.DiskSize - _position);
        var numRead = 0;

        while (numRead < maxToRead)
        {
            var block = (int)(_position / _fileHeader.BlockSize);
            var offsetInBlock = (int)(_position % _fileHeader.BlockSize);

            var toRead = Math.Min(maxToRead - numRead, _fileHeader.BlockSize - offsetInBlock);

            if (_blockTable[block] == BlockFree)
            {
                // TODO: Use parent
                buffer.Span.Slice(numRead, toRead).Clear();
            }
            else if (_blockTable[block] == BlockZero)
            {
                buffer.Span.Slice(numRead, toRead).Clear();
            }
            else
            {
                var blockOffset = _blockTable[block] * (_fileHeader.BlockSize + _fileHeader.BlockExtraSize);
                var filePos = _fileHeader.DataOffset + _fileHeader.BlockExtraSize + blockOffset +
                               offsetInBlock;
                _fileStream.Position = filePos;
                await _fileStream.ReadExactlyAsync(buffer.Slice(numRead, toRead), cancellationToken).ConfigureAwait(false);
            }

            _position += toRead;
            numRead += toRead;
        }

        return numRead;
    }

    public override int Read(Span<byte> buffer)
    {
        CheckDisposed();

        if (_atEof || _position > _fileHeader.DiskSize)
        {
            _atEof = true;
            throw new IOException("Attempt to read beyond end of file");
        }

        if (_position == _fileHeader.DiskSize)
        {
            _atEof = true;
            return 0;
        }

        var maxToRead = (int)Math.Min(buffer.Length, _fileHeader.DiskSize - _position);
        var numRead = 0;

        while (numRead < maxToRead)
        {
            var block = (int)(_position / _fileHeader.BlockSize);
            var offsetInBlock = (int)(_position % _fileHeader.BlockSize);

            var toRead = Math.Min(maxToRead - numRead, _fileHeader.BlockSize - offsetInBlock);

            if (_blockTable[block] == BlockFree)
            {
                // TODO: Use parent
                buffer.Slice(numRead, toRead).Clear();
            }
            else if (_blockTable[block] == BlockZero)
            {
                buffer.Slice(numRead, toRead).Clear();
            }
            else
            {
                var blockOffset = _blockTable[block] * (_fileHeader.BlockSize + _fileHeader.BlockExtraSize);
                var filePos = _fileHeader.DataOffset + _fileHeader.BlockExtraSize + blockOffset +
                               offsetInBlock;
                _fileStream.Position = filePos;
                _fileStream.ReadExactly(buffer.Slice(numRead, toRead));
            }

            _position += toRead;
            numRead += toRead;
        }

        return numRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        CheckDisposed();

        var effectiveOffset = offset;
        if (origin == SeekOrigin.Current)
        {
            effectiveOffset += _position;
        }
        else if (origin == SeekOrigin.End)
        {
            effectiveOffset += _fileHeader.DiskSize;
        }

        _atEof = false;

        if (effectiveOffset < 0)
        {
            throw new IOException("Attempt to move before beginning of disk");
        }

        _position = effectiveOffset;
        return _position;
    }

    public override void SetLength(long value)
    {
        CheckDisposed();
        if (Length != value)
        {
            throw new NotImplementedException("Cannot modify virtual length of vdi image");
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        CheckDisposed();

        if (!CanWrite)
        {
            throw new IOException("Attempt to write to read-only stream");
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Attempt to write negative number of bytes");
        }

        if (_atEof || _position + count > _fileHeader.DiskSize)
        {
            _atEof = true;
            throw new IOException("Attempt to write beyond end of file");
        }

        // On first write, notify event listeners - they just get to find out that some
        // write occurred, not about each write.
        if (!_writeNotified)
        {
            OnWriteOccurred();
            _writeNotified = true;
        }

        var numWritten = 0;
        while (numWritten < count)
        {
            var block = (int)(_position / _fileHeader.BlockSize);
            var offsetInBlock = (int)(_position % _fileHeader.BlockSize);

            var toWrite = Math.Min(count - numWritten, _fileHeader.BlockSize - offsetInBlock);

            // Optimize away zero-writes
            if (_blockTable[block] == BlockZero
                || (_blockTable[block] == BlockFree && toWrite == _fileHeader.BlockSize))
            {
                if (Utilities.IsAllZeros(buffer, offset + numWritten, toWrite))
                {
                    numWritten += toWrite;
                    _position += toWrite;
                    continue;
                }
            }

            if (_blockTable[block] == BlockFree || _blockTable[block] == BlockZero)
            {
                var writeBuffer = buffer;
                var writeBufferOffset = offset + numWritten;

                byte[] tempBuffer = null;
                try
                {
                    if (toWrite != _fileHeader.BlockSize)
                    {
                        writeBuffer = tempBuffer = ArrayPool<byte>.Shared.Rent(_fileHeader.BlockSize);

                        Array.Clear(tempBuffer, 0, _fileHeader.BlockSize);

                        if (_blockTable[block] == BlockFree)
                        {
                            // TODO: Use parent stream data...
                        }

                        // Copy actual data into temporary buffer, then this is a full block write.
                        System.Buffer.BlockCopy(buffer, offset + numWritten, writeBuffer, offsetInBlock, toWrite);
                        writeBufferOffset = 0;
                    }

                    var blockOffset = (long)_fileHeader.BlocksAllocated *
                                       (_fileHeader.BlockSize + _fileHeader.BlockExtraSize);
                    var filePos = _fileHeader.DataOffset + _fileHeader.BlockExtraSize + blockOffset;

                    _fileStream.Position = filePos;
                    _fileStream.Write(writeBuffer, writeBufferOffset, _fileHeader.BlockSize);
                }
                finally
                {
                    if (tempBuffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(tempBuffer);
                    }
                }

                _blockTable[block] = (uint)_fileHeader.BlocksAllocated;

                // Update the file header on disk, to indicate where the next free block is
                _fileHeader.BlocksAllocated++;
                _fileStream.Position = PreHeaderRecord.Size;
                _fileHeader.Write(_fileStream);

                // Update the block table on disk, to indicate where this block is
                WriteBlockTableEntry(block);
            }
            else
            {
                // Existing block, simply overwrite the existing data
                var blockOffset = _blockTable[block] * (_fileHeader.BlockSize + _fileHeader.BlockExtraSize);
                var filePos = _fileHeader.DataOffset + _fileHeader.BlockExtraSize + blockOffset +
                               offsetInBlock;
                _fileStream.Position = filePos;
                _fileStream.Write(buffer, offset + numWritten, toWrite);
            }

            numWritten += toWrite;
            _position += toWrite;
        }
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        CheckDisposed();

        if (!CanWrite)
        {
            throw new IOException("Attempt to write to read-only stream");
        }

        var count = buffer.Length;

        if (_atEof || _position + count > _fileHeader.DiskSize)
        {
            _atEof = true;
            throw new IOException("Attempt to write beyond end of file");
        }

        // On first write, notify event listeners - they just get to find out that some
        // write occurred, not about each write.
        if (!_writeNotified)
        {
            OnWriteOccurred();
            _writeNotified = true;
        }

        var numWritten = 0;
        while (numWritten < count)
        {
            var block = (int)(_position / _fileHeader.BlockSize);
            var offsetInBlock = (int)(_position % _fileHeader.BlockSize);

            var toWrite = Math.Min(count - numWritten, _fileHeader.BlockSize - offsetInBlock);

            // Optimize away zero-writes
            if (_blockTable[block] == BlockZero
                || (_blockTable[block] == BlockFree && toWrite == _fileHeader.BlockSize))
            {
                if (Utilities.IsAllZeros(buffer.Slice(numWritten, toWrite)))
                {
                    numWritten += toWrite;
                    _position += toWrite;
                    continue;
                }
            }

            if (_blockTable[block] == BlockFree || _blockTable[block] == BlockZero)
            {
                var writeBuffer = buffer;
                var writeBufferOffset = numWritten;

                byte[] tempBuffer = null;
                try
                {
                    if (toWrite != _fileHeader.BlockSize)
                    {
                        writeBuffer = tempBuffer = ArrayPool<byte>.Shared.Rent(_fileHeader.BlockSize);

                        Array.Clear(tempBuffer, 0, _fileHeader.BlockSize);

                        if (_blockTable[block] == BlockFree)
                        {
                            // TODO: Use parent stream data...
                        }

                        // Copy actual data into temporary buffer, then this is a full block write.
                        buffer.Slice(numWritten, toWrite).CopyTo(tempBuffer.AsSpan(offsetInBlock));

                        writeBufferOffset = 0;
                    }

                    var blockOffset = (long)_fileHeader.BlocksAllocated *
                                       (_fileHeader.BlockSize + _fileHeader.BlockExtraSize);
                    var filePos = _fileHeader.DataOffset + _fileHeader.BlockExtraSize + blockOffset;

                    _fileStream.Position = filePos;
                    _fileStream.Write(writeBuffer.Slice(writeBufferOffset, _fileHeader.BlockSize));
                }
                finally
                {
                    if (tempBuffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(tempBuffer);
                    }
                }

                _blockTable[block] = (uint)_fileHeader.BlocksAllocated;

                // Update the file header on disk, to indicate where the next free block is
                _fileHeader.BlocksAllocated++;
                _fileStream.Position = PreHeaderRecord.Size;
                _fileHeader.Write(_fileStream);

                // Update the block table on disk, to indicate where this block is
                WriteBlockTableEntry(block);
            }
            else
            {
                // Existing block, simply overwrite the existing data
                var blockOffset = _blockTable[block] * (_fileHeader.BlockSize + _fileHeader.BlockExtraSize);
                var filePos = _fileHeader.DataOffset + _fileHeader.BlockExtraSize + blockOffset +
                               offsetInBlock;
                _fileStream.Position = filePos;
                _fileStream.Write(buffer.Slice(numWritten, toWrite));
            }

            numWritten += toWrite;
            _position += toWrite;
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        CheckDisposed();

        if (!CanWrite)
        {
            throw new IOException("Attempt to write to read-only stream");
        }

        var count = buffer.Length;

        if (_atEof || _position + count > _fileHeader.DiskSize)
        {
            _atEof = true;
            throw new IOException("Attempt to write beyond end of file");
        }

        // On first write, notify event listeners - they just get to find out that some
        // write occurred, not about each write.
        if (!_writeNotified)
        {
            OnWriteOccurred();
            _writeNotified = true;
        }

        var numWritten = 0;
        while (numWritten < count)
        {
            var block = (int)(_position / _fileHeader.BlockSize);
            var offsetInBlock = (int)(_position % _fileHeader.BlockSize);

            var toWrite = Math.Min(count - numWritten, _fileHeader.BlockSize - offsetInBlock);

            // Optimize away zero-writes
            if (_blockTable[block] == BlockZero
                || (_blockTable[block] == BlockFree && toWrite == _fileHeader.BlockSize))
            {
                if (Utilities.IsAllZeros(buffer.Slice(numWritten, toWrite).Span))
                {
                    numWritten += toWrite;
                    _position += toWrite;
                    continue;
                }
            }

            if (_blockTable[block] == BlockFree || _blockTable[block] == BlockZero)
            {
                var writeBuffer = buffer;
                var writeBufferOffset = numWritten;

                byte[] tempBuffer = null;
                try
                {
                    if (toWrite != _fileHeader.BlockSize)
                    {
                        writeBuffer = tempBuffer = ArrayPool<byte>.Shared.Rent(_fileHeader.BlockSize);

                        Array.Clear(tempBuffer, 0, _fileHeader.BlockSize);

                        if (_blockTable[block] == BlockFree)
                        {
                            // TODO: Use parent stream data...
                        }

                        // Copy actual data into temporary buffer, then this is a full block write.
                        buffer.Span.Slice(numWritten, toWrite).CopyTo(tempBuffer.AsSpan(offsetInBlock));

                        writeBufferOffset = 0;
                    }

                    var blockOffset = (long)_fileHeader.BlocksAllocated *
                                       (_fileHeader.BlockSize + _fileHeader.BlockExtraSize);
                    var filePos = _fileHeader.DataOffset + _fileHeader.BlockExtraSize + blockOffset;

                    _fileStream.Position = filePos;
                    await _fileStream.WriteAsync(writeBuffer.Slice(writeBufferOffset, _fileHeader.BlockSize), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (tempBuffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(tempBuffer);
                    }
                }

                _blockTable[block] = (uint)_fileHeader.BlocksAllocated;

                // Update the file header on disk, to indicate where the next free block is
                _fileHeader.BlocksAllocated++;
                _fileStream.Position = PreHeaderRecord.Size;
                await _fileHeader.WriteAsync(_fileStream, cancellationToken).ConfigureAwait(false);

                // Update the block table on disk, to indicate where this block is
                await WriteBlockTableEntryAsync(block, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Existing block, simply overwrite the existing data
                var blockOffset = _blockTable[block] * (_fileHeader.BlockSize + _fileHeader.BlockExtraSize);
                var filePos = _fileHeader.DataOffset + _fileHeader.BlockExtraSize + blockOffset +
                               offsetInBlock;
                _fileStream.Position = filePos;
                await _fileStream.WriteAsync(buffer.Slice(numWritten, toWrite), cancellationToken).ConfigureAwait(false);
            }

            numWritten += toWrite;
            _position += toWrite;
        }
    }

    protected override void Dispose(bool disposing)
    {
        _isDisposed = true;
        try
        {
            if (disposing && _ownsStream == Ownership.Dispose && _fileStream != null)
            {
                _fileStream.Dispose();
                _fileStream = null;
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    protected virtual void OnWriteOccurred()
    {
        var handler = WriteOccurred;
        if (handler != null)
        {
            handler(this, null);
        }
    }

    private void ReadBlockTable()
    {
        _fileStream.Position = _fileHeader.BlocksOffset;

        var buffer = _fileStream.ReadExactly(_fileHeader.BlockCount * 4);

        _blockTable = new uint[_fileHeader.BlockCount];
        for (var i = 0; i < _fileHeader.BlockCount; ++i)
        {
            _blockTable[i] = EndianUtilities.ToUInt32LittleEndian(buffer, i * 4);
        }
    }

    private void WriteBlockTableEntry(int block)
    {
        Span<byte> buffer = stackalloc byte[4];
        EndianUtilities.WriteBytesLittleEndian(_blockTable[block], buffer);

        _fileStream.Position = _fileHeader.BlocksOffset + block * 4;
        _fileStream.Write(buffer);
    }

    private async ValueTask WriteBlockTableEntryAsync(int block, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            EndianUtilities.WriteBytesLittleEndian(_blockTable[block], buffer);

            _fileStream.Position = _fileHeader.BlocksOffset + block * 4;
            await _fileStream.WriteAsync(buffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void CheckDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException("DiskStream", "Attempt to use disposed stream");
        }
    }
}