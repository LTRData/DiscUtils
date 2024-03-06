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

namespace DiscUtils.Registry;

internal sealed class KeyNodeCell : Cell
{
    public int ClassNameIndex;
    public int ClassNameLength;
    public RegistryKeyFlags Flags;

    public int IndexInParent;

    /// <summary>
    /// Number of bytes to represent largest subkey name in Unicode - no null terminator.
    /// </summary>
    public int MaxSubKeyNameBytes;

    /// <summary>
    /// Number of bytes to represent largest value content (strings in Unicode, with null terminator - if stored).
    /// </summary>
    public int MaxValDataBytes;

    /// <summary>
    /// Number of bytes to represent largest value name in Unicode - no null terminator.
    /// </summary>
    public int MaxValNameBytes;

    public string Name;
    public int NumSubKeys;
    public int NumValues;
    public int ParentIndex;
    public int SecurityIndex;
    public int SubKeysIndex;
    public DateTime Timestamp;
    public int ValueListIndex;

    public KeyNodeCell(string name, int parentCellIndex)
        : this(-1)
    {
        Flags = RegistryKeyFlags.Normal;
        Timestamp = DateTime.UtcNow;
        ParentIndex = parentCellIndex;
        SubKeysIndex = -1;
        ValueListIndex = -1;
        SecurityIndex = -1;
        ClassNameIndex = -1;
        Name = name;
    }

    public KeyNodeCell(int index)
        : base(index) {}

    public override int Size
    {
        get { return 0x4C + Name.Length; }
    }

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        Flags = (RegistryKeyFlags)EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x02));
        Timestamp = DateTime.FromFileTimeUtc(EndianUtilities.ToInt64LittleEndian(buffer.Slice(0x04)));
        ParentIndex = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x10));
        NumSubKeys = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x14));
        SubKeysIndex = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x1C));
        NumValues = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x24));
        ValueListIndex = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x28));
        SecurityIndex = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x2C));
        ClassNameIndex = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x30));
        MaxSubKeyNameBytes = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x34));
        MaxValNameBytes = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x3C));
        MaxValDataBytes = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x40));
        IndexInParent = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x44));
        int nameLength = EndianUtilities.ToInt16LittleEndian(buffer.Slice(0x48));
        ClassNameLength = EndianUtilities.ToInt16LittleEndian(buffer.Slice(0x4A));
        Name = latin1Encoding.GetString(buffer.Slice(0x4C, nameLength));

        return 0x4C + nameLength;
    }

    public override void WriteTo(Span<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        "nk"u8.CopyTo(buffer.Slice(0, 2));
        EndianUtilities.WriteBytesLittleEndian((ushort)Flags, buffer.Slice(0x02));
        EndianUtilities.WriteBytesLittleEndian(Timestamp.ToFileTimeUtc(), buffer.Slice(0x04));
        EndianUtilities.WriteBytesLittleEndian(ParentIndex, buffer.Slice(0x10));
        EndianUtilities.WriteBytesLittleEndian(NumSubKeys, buffer.Slice(0x14));
        EndianUtilities.WriteBytesLittleEndian(SubKeysIndex, buffer.Slice(0x1C));
        EndianUtilities.WriteBytesLittleEndian(NumValues, buffer.Slice(0x24));
        EndianUtilities.WriteBytesLittleEndian(ValueListIndex, buffer.Slice(0x28));
        EndianUtilities.WriteBytesLittleEndian(SecurityIndex, buffer.Slice(0x2C));
        EndianUtilities.WriteBytesLittleEndian(ClassNameIndex, buffer.Slice(0x30));
        EndianUtilities.WriteBytesLittleEndian(IndexInParent, buffer.Slice(0x44));
        EndianUtilities.WriteBytesLittleEndian((ushort)Name.Length, buffer.Slice(0x48));
        EndianUtilities.WriteBytesLittleEndian(ClassNameLength, buffer.Slice(0x4A));
        latin1Encoding.GetBytes(Name, buffer.Slice(0x4C, Name.Length));
    }

    public override string ToString()
    {
        return $"Key:{Name}[{Flags}] <{Timestamp}>";
    }
}