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
using DiscUtils.CoreCompat;
using DiscUtils.Streams;

namespace DiscUtils.SquashFs;

internal class DirectoryRecord : IByteArraySerializable
{
    public short InodeNumber;
    public string Name;
    public ushort Offset;
    public InodeType Type;

    public int Size
    {
        get { return 8 + Name.Length; }
    }

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        throw new NotImplementedException();
    }

    public void WriteTo(Span<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        EndianUtilities.WriteBytesLittleEndian(Offset, buffer);
        EndianUtilities.WriteBytesLittleEndian(InodeNumber, buffer.Slice(2));
        EndianUtilities.WriteBytesLittleEndian((ushort)Type, buffer.Slice(4));
        EndianUtilities.WriteBytesLittleEndian((ushort)(Name.Length - 1), buffer.Slice(6));
        latin1Encoding.GetBytes(Name, buffer.Slice(8, Name.Length));
    }

    public static DirectoryRecord ReadFrom(MetablockReader reader)
    {
        var result = new DirectoryRecord
        {
            Offset = reader.ReadUShort(),
            InodeNumber = reader.ReadShort(),
            Type = (InodeType)reader.ReadUShort()
        };
        var size = reader.ReadUShort();
        result.Name = reader.ReadString(size + 1).SanitizeFileName();

        return result;
    }
}