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
using DiscUtils.Streams;

namespace DiscUtils.Ntfs;

internal sealed class IndexEntry
{
    public const int EndNodeSize = 0x18;
    private byte[] _dataBuffer;

    private IndexEntryFlags _flags;

    private byte[] _keyBuffer;
    private long _vcn; // Only valid if Node flag set

    public IndexEntry(bool isFileIndexEntry)
    {
        IsFileIndexEntry = isFileIndexEntry;
    }

    public IndexEntry(IndexEntry toCopy, byte[] newKey, byte[] newData)
    {
        IsFileIndexEntry = toCopy.IsFileIndexEntry;
        _flags = toCopy._flags;
        _vcn = toCopy._vcn;
        _keyBuffer = newKey;
        _dataBuffer = newData;
    }

    public IndexEntry(byte[] key, byte[] data, bool isFileIndexEntry)
    {
        IsFileIndexEntry = isFileIndexEntry;
        _flags = IndexEntryFlags.None;
        _keyBuffer = key;
        _dataBuffer = data;
    }

    public long ChildrenVirtualCluster
    {
        get => _vcn;
        set => _vcn = value;
    }

    public byte[] DataBuffer
    {
        get => _dataBuffer;
        set => _dataBuffer = value;
    }

    public IndexEntryFlags Flags
    {
        get => _flags;
        set => _flags = value;
    }

    private readonly bool IsFileIndexEntry;

    public byte[] KeyBuffer
    {
        get => _keyBuffer;
        set => _keyBuffer = value;
    }

    public int Size
    {
        get
        {
            var size = 0x10; // start of variable data

            if ((_flags & IndexEntryFlags.End) == 0)
            {
                size += _keyBuffer.Length;
                size += IsFileIndexEntry ? 0 : _dataBuffer.Length;
            }

            size = MathUtilities.RoundUp(size, 8);

            if ((_flags & IndexEntryFlags.Node) != 0)
            {
                size += 8;
            }

            return size;
        }
    }

    public void Read(ReadOnlySpan<byte> buffer)
    {
        var dataOffset = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x00));
        var dataLength = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x02));
        var length = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x08));
        var keyLength = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x0A));
        _flags = (IndexEntryFlags)EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x0C));

        if ((_flags & IndexEntryFlags.End) == 0)
        {
            _keyBuffer = new byte[keyLength];
            buffer.Slice(0x10, keyLength).CopyTo(_keyBuffer);

            if (IsFileIndexEntry)
            {
                // Special case, for file indexes, the MFT ref is held where the data offset & length go
                _dataBuffer = new byte[8];
                buffer.Slice(0x00, 8).CopyTo(_dataBuffer);
            }
            else
            {
                _dataBuffer = new byte[dataLength];
                buffer.Slice(0x10 + keyLength, dataLength).CopyTo(_dataBuffer);
            }
        }

        if ((_flags & IndexEntryFlags.Node) != 0)
        {
            _vcn = EndianUtilities.ToInt64LittleEndian(buffer.Slice(length - 8));
        }
    }

    public void WriteTo(Span<byte> buffer)
    {
        var length = (ushort)Size;

        if ((_flags & IndexEntryFlags.End) == 0)
        {
            var keyLength = (ushort)_keyBuffer.Length;

            if (IsFileIndexEntry)
            {
                _dataBuffer.AsSpan(0, 8).CopyTo(buffer);
            }
            else
            {
                var dataOffset = (ushort)(IsFileIndexEntry ? 0 : 0x10 + keyLength);
                var dataLength = (ushort)_dataBuffer.Length;

                EndianUtilities.WriteBytesLittleEndian(dataOffset, buffer);
                EndianUtilities.WriteBytesLittleEndian(dataLength, buffer.Slice(0x02));
                _dataBuffer.AsSpan().CopyTo(buffer.Slice(dataOffset));
            }

            EndianUtilities.WriteBytesLittleEndian(keyLength, buffer.Slice(0x0A));
            _keyBuffer.AsSpan().CopyTo(buffer.Slice(0x10));
        }
        else
        {
            EndianUtilities.WriteBytesLittleEndian((ushort)0, buffer); // dataOffset
            EndianUtilities.WriteBytesLittleEndian((ushort)0, buffer.Slice(0x02)); // dataLength
            EndianUtilities.WriteBytesLittleEndian((ushort)0, buffer.Slice(0x0A)); // keyLength
        }

        EndianUtilities.WriteBytesLittleEndian(length, buffer.Slice(0x08));
        EndianUtilities.WriteBytesLittleEndian((ushort)_flags, buffer.Slice(0x0C));
        if ((_flags & IndexEntryFlags.Node) != 0)
        {
            EndianUtilities.WriteBytesLittleEndian(_vcn, buffer.Slice(length - 8));
        }
    }
}