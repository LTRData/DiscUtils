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

    public IndexEntry(bool isFileIndexEntry)
    {
        IsFileIndexEntry = isFileIndexEntry;
    }

    public IndexEntry(IndexEntry toCopy, byte[] newKey, byte[] newData)
    {
        IsFileIndexEntry = toCopy.IsFileIndexEntry;
        Flags = toCopy.Flags;
        ChildrenVirtualCluster = toCopy.ChildrenVirtualCluster;
        KeyBuffer = newKey;
        DataBuffer = newData;
    }

    public IndexEntry(byte[] key, byte[] data, bool isFileIndexEntry)
    {
        IsFileIndexEntry = isFileIndexEntry;
        Flags = IndexEntryFlags.None;
        KeyBuffer = key;
        DataBuffer = data;
    }

    public long ChildrenVirtualCluster { get; set; }

    public byte[] DataBuffer { get; set; }

    public IndexEntryFlags Flags { get; set; }

    private readonly bool IsFileIndexEntry;

    public byte[] KeyBuffer { get; set; }

    public int Size
    {
        get
        {
            var size = 0x10; // start of variable data

            if ((Flags & IndexEntryFlags.End) == 0)
            {
                size += KeyBuffer.Length;
                size += IsFileIndexEntry ? 0 : DataBuffer.Length;
            }

            size = MathUtilities.RoundUp(size, 8);

            if ((Flags & IndexEntryFlags.Node) != 0)
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
        Flags = (IndexEntryFlags)EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x0C));

        if ((Flags & IndexEntryFlags.End) == 0)
        {
            KeyBuffer = StreamUtilities.GetUninitializedArray<byte>(keyLength);
            buffer.Slice(0x10, keyLength).CopyTo(KeyBuffer);

            if (IsFileIndexEntry)
            {
                // Special case, for file indexes, the MFT ref is held where the data offset & length go
                DataBuffer = StreamUtilities.GetUninitializedArray<byte>(8);
                buffer.Slice(0x00, 8).CopyTo(DataBuffer);
            }
            else
            {
                DataBuffer = StreamUtilities.GetUninitializedArray<byte>(dataLength);
                buffer.Slice(0x10 + keyLength, dataLength).CopyTo(DataBuffer);
            }
        }

        if ((Flags & IndexEntryFlags.Node) != 0)
        {
            ChildrenVirtualCluster = EndianUtilities.ToInt64LittleEndian(buffer.Slice(length - 8));
        }
    }

    public void WriteTo(Span<byte> buffer)
    {
        var length = (ushort)Size;

        if ((Flags & IndexEntryFlags.End) == 0)
        {
            var keyLength = (ushort)KeyBuffer.Length;

            if (IsFileIndexEntry)
            {
                DataBuffer.AsSpan(0, 8).CopyTo(buffer);
            }
            else
            {
                var dataOffset = (ushort)(IsFileIndexEntry ? 0 : 0x10 + keyLength);
                var dataLength = (ushort)DataBuffer.Length;

                EndianUtilities.WriteBytesLittleEndian(dataOffset, buffer);
                EndianUtilities.WriteBytesLittleEndian(dataLength, buffer.Slice(0x02));
                DataBuffer.AsSpan().CopyTo(buffer.Slice(dataOffset));
            }

            EndianUtilities.WriteBytesLittleEndian(keyLength, buffer.Slice(0x0A));
            KeyBuffer.AsSpan().CopyTo(buffer.Slice(0x10));
        }
        else
        {
            EndianUtilities.WriteBytesLittleEndian((ushort)0, buffer); // dataOffset
            EndianUtilities.WriteBytesLittleEndian((ushort)0, buffer.Slice(0x02)); // dataLength
            EndianUtilities.WriteBytesLittleEndian((ushort)0, buffer.Slice(0x0A)); // keyLength
        }

        EndianUtilities.WriteBytesLittleEndian(length, buffer.Slice(0x08));
        EndianUtilities.WriteBytesLittleEndian((ushort)Flags, buffer.Slice(0x0C));
        if ((Flags & IndexEntryFlags.Node) != 0)
        {
            EndianUtilities.WriteBytesLittleEndian(ChildrenVirtualCluster, buffer.Slice(length - 8));
        }
    }
}