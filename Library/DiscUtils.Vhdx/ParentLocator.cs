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
using System.Text;
using DiscUtils.Streams;

namespace DiscUtils.Vhdx;

internal sealed class ParentLocator : IByteArraySerializable
{
    private static readonly Guid LocatorTypeGuid = new Guid("B04AEFB7-D19E-4A81-B789-25B8E9445913");

    public ushort Count;
    public Guid LocatorType = LocatorTypeGuid;
    public ushort Reserved = 0;

    public Dictionary<string, string> Entries { get; private set; } = new Dictionary<string, string>();

    public ParentLocator()
    {
    }

    public ParentLocator(String parentUid, String relativePath, String absolutePath)
    {
        Entries.Add("parent_linkage", parentUid);
        Entries.Add("relative_path", relativePath);
        if (absolutePath.Length > 3 && absolutePath[1] == ':' && absolutePath[2] == '\\')
        {
            absolutePath = $@"\\?\{absolutePath}";
        }
        else if (absolutePath.StartsWith(@"\\", StringComparison.Ordinal)
            && !(absolutePath.StartsWith(@"\\?\", StringComparison.Ordinal)
            || absolutePath.StartsWith(@"\\.\", StringComparison.Ordinal)))
        {
#if NET5_0_OR_GREATER
            absolutePath = $@"\\?\UNC\{absolutePath.AsSpan(2)}";
#else
            absolutePath = $@"\\?\UNC\{absolutePath.Substring(2)}";
#endif
        }
        Entries.Add("absolute_win32_path", absolutePath);
    }

    public int Size
    {
        get
        {
            var size = 20 + Entries.Count * 12;

            foreach (var entry in Entries)
            {
                size += Encoding.Unicode.GetByteCount(entry.Key);
                size += Encoding.Unicode.GetByteCount(entry.Value);
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

        Entries = new Dictionary<string, string>();

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
            var keyData = EndianUtilities.StringToLittleEndianUnicodeBytesToString(entry.Key.AsSpan());
            var valueData = EndianUtilities.StringToLittleEndianUnicodeBytesToString(entry.Value.AsSpan());

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