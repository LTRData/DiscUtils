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
using DiscUtils.Streams;

namespace DiscUtils.Vhdx;

internal sealed class MetadataEntry : IByteArraySerializable
{
    public MetadataEntryFlags Flags;
    public Guid ItemId;
    public uint Length;
    public uint Offset;
    public uint Reserved;

    public int Size => 32;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        ItemId = EndianUtilities.ToGuidLittleEndian(buffer);
        Offset = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(16));
        Length = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(20));
        Flags = (MetadataEntryFlags)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(24));
        Reserved = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(28));

        return 32;
    }

    public void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian(ItemId, buffer);
        EndianUtilities.WriteBytesLittleEndian(Offset, buffer.Slice(16));
        EndianUtilities.WriteBytesLittleEndian(Length, buffer.Slice(20));
        EndianUtilities.WriteBytesLittleEndian((uint)Flags, buffer.Slice(24));
        EndianUtilities.WriteBytesLittleEndian(Reserved, buffer.Slice(28));
    }
}