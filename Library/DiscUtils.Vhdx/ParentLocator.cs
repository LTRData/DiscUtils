//
// Copyright (c) 2008-2012, Kenneth Bell
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
using System.Runtime.InteropServices;
using DiscUtils.Streams;

namespace DiscUtils.Vhdx;

internal sealed class ParentLocator : IByteArraySerializable
{
    private static readonly Guid LocatorTypeGuid = new("B04AEFB7-D19E-4A81-B789-25B8E9445913");

    public ushort Count;
    public Guid LocatorType = LocatorTypeGuid;
    public ushort Reserved = 0;

    public Dictionary<string, string> Entries { get; private set; } = [];

    public ParentLocator()
    {
    }

    public ParentLocator(string parentUid, string relativePath, string absolutePath)
    {
        Entries.Add("parent_linkage", parentUid);

        Entries.Add("absolute_win32_path", absolutePath);

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            Entries.Add("relative_path", relativePath);
        }
    }

    public int Size
    {
        get
        {
            var size = 20 + Entries.Count * 12;

            foreach (var entry in Entries)
            {
                size += MemoryMarshal.AsBytes(entry.Key.AsSpan()).Length;
                size += MemoryMarshal.AsBytes(entry.Value.AsSpan()).Length;
            }

            return size;
        }
    }

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        LocatorType = EndianUtilities.ToGuidLittleEndian(buffer);
        if (LocatorType != LocatorTypeGuid)
        {
            throw new IOException($"Unrecognized Parent Locator type: {LocatorType}");
        }

        Entries = [];

        Count = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(18));
        for (ushort i = 0; i < Count; ++i)
        {
            var kvOffset = 20 + i * 12;
            var keyOffset = EndianUtilities.ToInt32LittleEndian(buffer.Slice(kvOffset + 0));
            var valueOffset = EndianUtilities.ToInt32LittleEndian(buffer.Slice(kvOffset + 4));
            int keyLength = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(kvOffset + 8));
            int valueLength = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(kvOffset + 10));

            var key = EndianUtilities.LittleEndianUnicodeBytesToString(buffer.Slice(keyOffset, keyLength));
            var value = EndianUtilities.LittleEndianUnicodeBytesToString(buffer.Slice(valueOffset, valueLength));

            Entries[key] = value;
        }

        return 0;
    }

    public void WriteTo(Span<byte> buffer)
    {
        Count = (ushort)Entries.Count;

        EndianUtilities.WriteBytesLittleEndian(LocatorType, buffer);
        EndianUtilities.WriteBytesLittleEndian(Reserved, buffer.Slice(16));
        EndianUtilities.WriteBytesLittleEndian(Count, buffer.Slice(18));

        var entryOffset = 0;
        var item = 0;

        foreach (var entry in Entries)
        {
            var keyData = EndianUtilities.StringToLittleEndianUnicodeBytes(entry.Key);
            var valueData = EndianUtilities.StringToLittleEndianUnicodeBytes(entry.Value);

            keyData.CopyTo(buffer.Slice(20 + Count * 12 + entryOffset));
            EndianUtilities.WriteBytesLittleEndian((ushort)(20 + Count * 12 + entryOffset), buffer.Slice(20 + item * 12));
            EndianUtilities.WriteBytesLittleEndian((ushort)keyData.Length, buffer.Slice(20 + item * 12 + 8));
            entryOffset += keyData.Length;

            valueData.CopyTo(buffer.Slice(20 + Count * 12 + entryOffset));
            EndianUtilities.WriteBytesLittleEndian((ushort)(20 + Count * 12 + entryOffset), buffer.Slice(20 + item * 12 + 4));
            EndianUtilities.WriteBytesLittleEndian((ushort)valueData.Length, buffer.Slice(20 + item * 12 + 10));
            entryOffset += valueData.Length;

            ++item;
        }
    }
}