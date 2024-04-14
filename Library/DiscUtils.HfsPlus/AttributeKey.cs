//
// Copyright (c) 2014, Quamotion
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

namespace DiscUtils.HfsPlus;

internal class AttributeKey : BTreeKey
{
    private ushort _keyLength;
    //private ushort _pad;
    //private uint _startBlock;

    public AttributeKey() {}

    public AttributeKey(CatalogNodeId nodeId, string name)
    {
        FileId = nodeId;
        Name = name;
    }

    public CatalogNodeId FileId { get; private set; }

    public string Name { get; private set; }

    public override int Size => throw new NotImplementedException();

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        _keyLength = EndianUtilities.ToUInt16BigEndian(buffer);
        //_pad = EndianUtilities.ToUInt16BigEndian(buffer.Slice(2));
        FileId = new CatalogNodeId(EndianUtilities.ToUInt32BigEndian(buffer.Slice(4)));
        //_startBlock = EndianUtilities.ToUInt32BigEndian(buffer.Slice(8));
        Name = HfsPlusUtilities.ReadUniStr255(buffer.Slice(12));

        return _keyLength + 2;
    }

    public override void WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }

    public override int CompareTo(BTreeKey other)
    {
        return CompareTo(other as AttributeKey);
    }

    public int CompareTo(AttributeKey other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        if (FileId != other.FileId)
        {
            return FileId < other.FileId ? -1 : 1;
        }

        return HfsPlusUtilities.FastUnicodeCompare(Name, other.Name);
    }

    public override string ToString()
    {
        return $"{Name} ({FileId})";
    }
}