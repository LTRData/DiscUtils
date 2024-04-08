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
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Registry;

/// <summary>
/// An internal structure within registry files, bins are the major unit of allocation in a registry hive.
/// </summary>
/// <remarks>Bins are divided into multiple cells, that contain actual registry data.</remarks>
internal sealed class Bin
{
    private readonly byte[] _buffer;
    private readonly Stream _fileStream;

    private readonly List<Range<int, int>> _freeCells;

    private readonly BinHeader _header;
    private readonly RegistryHive _hive;
    private readonly long _streamPos;

    public Bin(RegistryHive hive, Stream stream)
    {
        _hive = hive;
        _fileStream = stream;
        _streamPos = stream.Position;

        stream.Position = _streamPos;
        Span<byte> buffer = stackalloc byte[0x20];
        stream.ReadExactly(buffer);
        _header = new BinHeader();
        _header.ReadFrom(buffer);

        _fileStream.Position = _streamPos;
        _buffer = _fileStream.ReadExactly(_header.BinSize);

        // Gather list of all free cells.
        _freeCells = new List<Range<int, int>>();
        var pos = 0x20;
        while (pos < _buffer.Length)
        {
            var size = EndianUtilities.ToInt32LittleEndian(_buffer, pos);
            if (size > 0)
            {
                _freeCells.Add(new Range<int, int>(pos, size));
            }

            pos += Math.Abs(size);
        }
    }

    public Cell TryGetCell(int index)
    {
        var size = EndianUtilities.ToInt32LittleEndian(_buffer, index - _header.FileOffset);
        if (size >= 0)
        {
            return null;
        }

        return Cell.Parse(_hive, index, _buffer.AsSpan(index + 4 - _header.FileOffset));
    }

    public void FreeCell(int index)
    {
        var freeIndex = index - _header.FileOffset;

        var len = EndianUtilities.ToInt32LittleEndian(_buffer, freeIndex);
        if (len >= 0)
        {
            throw new ArgumentException("Attempt to free non-allocated cell");
        }

        len = Math.Abs(len);

        // If there's a free cell before this one, combine
        var i = 0;
        while (i < _freeCells.Count && _freeCells[i].Offset < freeIndex)
        {
            if (_freeCells[i].Offset + _freeCells[i].Count == freeIndex)
            {
                freeIndex = _freeCells[i].Offset;
                len += _freeCells[i].Count;
                _freeCells.RemoveAt(i);
            }
            else
            {
                ++i;
            }
        }

        // If there's a free cell after this one, combine
        if (i < _freeCells.Count && _freeCells[i].Offset == freeIndex + len)
        {
            len += _freeCells[i].Count;
            _freeCells.RemoveAt(i);
        }

        // Record the new free cell
        _freeCells.Insert(i, new Range<int, int>(freeIndex, len));

        // Free cells are indicated by length > 0
        EndianUtilities.WriteBytesLittleEndian(len, _buffer, freeIndex);

        _fileStream.Position = _streamPos + freeIndex;
        _fileStream.Write(_buffer, freeIndex, 4);
    }

    public bool UpdateCell(Cell cell)
    {
        var index = cell.Index - _header.FileOffset;
        var allocSize = Math.Abs(EndianUtilities.ToInt32LittleEndian(_buffer, index));

        var newSize = cell.Size + 4;
        if (newSize > allocSize)
        {
            return false;
        }

        cell.WriteTo(_buffer, index + 4);

        _fileStream.Position = _streamPos + index;
        _fileStream.Write(_buffer, index, newSize);

        return true;
    }

    public Span<byte> ReadRawCellData(int cellIndex, Span<byte> maxBytes)
    {
        var index = cellIndex - _header.FileOffset;
        var len = Math.Abs(EndianUtilities.ToInt32LittleEndian(_buffer, index));
        var result = maxBytes.Slice(0, Math.Min(len - 4, maxBytes.Length));
        _buffer.AsSpan(index + 4, result.Length).CopyTo(result);
        return result;
    }

    internal bool WriteRawCellData(int cellIndex, ReadOnlySpan<byte> data)
    {
        var index = cellIndex - _header.FileOffset;
        var allocSize = Math.Abs(EndianUtilities.ToInt32LittleEndian(_buffer, index));

        var newSize = data.Length + 4;
        if (newSize > allocSize)
        {
            return false;
        }

        data.CopyTo(_buffer.AsSpan(index + 4));

        _fileStream.Position = _streamPos + index;
        _fileStream.Write(_buffer, index, newSize);

        return true;
    }

    internal int AllocateCell(int size)
    {
        if (size < 8 || size % 8 != 0)
        {
            throw new ArgumentException("Invalid cell size");
        }

        // Very inefficient algorithm - will lead to fragmentation
        for (var i = 0; i < _freeCells.Count; ++i)
        {
            var result = _freeCells[i].Offset + _header.FileOffset;
            if (_freeCells[i].Count > size)
            {
                // Record the newly allocated cell
                EndianUtilities.WriteBytesLittleEndian(-size, _buffer, _freeCells[i].Offset);
                _fileStream.Position = _streamPos + _freeCells[i].Offset;
                _fileStream.Write(_buffer, _freeCells[i].Offset, 4);

                // Keep the remainder of the free buffer as unallocated
                _freeCells[i] = new Range<int, int>(_freeCells[i].Offset + size, _freeCells[i].Count - size);
                EndianUtilities.WriteBytesLittleEndian(_freeCells[i].Count, _buffer, _freeCells[i].Offset);
                _fileStream.Position = _streamPos + _freeCells[i].Offset;
                _fileStream.Write(_buffer, _freeCells[i].Offset, 4);

                return result;
            }

            if (_freeCells[i].Count == size)
            {
                // Record the whole of the free buffer as a newly allocated cell
                EndianUtilities.WriteBytesLittleEndian(-size, _buffer, _freeCells[i].Offset);
                _fileStream.Position = _streamPos + _freeCells[i].Offset;
                _fileStream.Write(_buffer, _freeCells[i].Offset, 4);

                _freeCells.RemoveAt(i);
                return result;
            }
        }

        return -1;
    }
}